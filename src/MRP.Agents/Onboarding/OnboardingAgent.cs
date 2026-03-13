using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Onboarding;

public class OnboardingAgent : AgentBase
{
    public override string Name => "OnboardingAgent";
    public override AgentType Type => AgentType.Onboarding;

    public OnboardingAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<OnboardingAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(30))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var merchantRepo = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var paynow = scope.ServiceProvider.GetRequiredService<IPaynowGateway>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<OnboardingInput>(task.InputPayload ?? "{}");
        var merchant = await merchantRepo.GetByIdAsync(input?.MerchantId ?? Guid.Empty, ct);

        if (merchant?.Integration is null)
        {
            return new AgentResult
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                IsSuccess = false,
                ErrorMessage = "Merchant or integration not found",
                CompletedAt = DateTime.UtcNow
            };
        }

        var issues = new List<string>();
        var integration = merchant.Integration;

        // Validate credentials
        if (string.IsNullOrWhiteSpace(integration.PaynowIntegrationId))
            issues.Add("Missing Paynow Integration ID");
        if (string.IsNullOrWhiteSpace(integration.PaynowIntegrationKey))
            issues.Add("Missing Paynow Integration Key");

        // Validate URLs
        if (!Uri.TryCreate(integration.ResultUrl, UriKind.Absolute, out var resultUri)
            || resultUri.Scheme != "https")
            issues.Add("Result URL must be a valid HTTPS URL");
        if (!Uri.TryCreate(integration.ReturnUrl, UriKind.Absolute, out _))
            issues.Add("Return URL must be a valid URL");

        // Test callback reachability
        bool callbackReachable = false;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var response = await http.GetAsync(integration.ResultUrl, ct);
            callbackReachable = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
        }
        catch
        {
            issues.Add("Callback URL is not reachable");
        }

        // Try test payment initiation
        bool paymentTestPassed = false;
        if (issues.Count == 0)
        {
            try
            {
                var testResult = await paynow.InitiatePaymentAsync(
                    integration, $"MRP-TEST-{Guid.NewGuid():N}", 0.01m,
                    "test@mrp.local", ct);
                paymentTestPassed = testResult.Success;
                if (!testResult.Success)
                    issues.Add($"Test payment failed: {testResult.Error}");
            }
            catch (Exception ex)
            {
                issues.Add($"Payment test exception: {ex.Message}");
            }
        }

        // Calculate health score
        int score = 100;
        score -= issues.Count * 15;
        if (!callbackReachable) score -= 20;
        if (!paymentTestPassed) score -= 25;
        score = Math.Clamp(score, 0, 100);

        // Update integration status
        integration.IsCallbackReachable = callbackReachable;
        integration.LastCallbackTestAt = DateTime.UtcNow;
        if (issues.Count > 0)
            integration.ConsecutiveFailures++;
        else
            integration.ConsecutiveFailures = 0;

        merchant.ReliabilityScore = score;
        await merchantRepo.UpdateAsync(merchant, ct);

        bool isValid = issues.Count == 0;
        await eventBus.PublishAsync(new MerchantOnboarded(merchant.Id, isValid), ct);

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = isValid,
            OutputPayload = JsonSerializer.Serialize(new
            {
                isValid,
                callbackReachable,
                paymentTestPassed,
                healthScore = score,
                issues
            }),
            CompletedAt = DateTime.UtcNow
        };
    }
}

public record OnboardingInput(Guid MerchantId);
