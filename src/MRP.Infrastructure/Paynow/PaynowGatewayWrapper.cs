using System.Text.Json;
using Microsoft.Extensions.Logging;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Webdev.Payments;

namespace MRP.Infrastructure.Paynow;

/// <summary>
/// Wraps the Paynow SDK with Polly resilience (retry + circuit breaker).
/// Registered as SINGLETON so the circuit breaker state is shared across all requests.
/// </summary>
public class PaynowGatewayWrapper : IPaynowGateway
{
    private readonly ILogger<PaynowGatewayWrapper> _logger;

    // Static policies so circuit breaker state is shared across all instances
    private static readonly AsyncRetryPolicy RetryPolicy = Policy
        .Handle<Exception>(ex => ex is not InvalidOperationException)
        .WaitAndRetryAsync(
            retryCount: 3,
            sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)));

    private static readonly AsyncCircuitBreakerPolicy CircuitBreaker = Policy
        .Handle<Exception>(ex => ex is not InvalidOperationException)
        .CircuitBreakerAsync(
            exceptionsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(60));

    private static readonly AsyncPolicy Resilience = Policy.WrapAsync(RetryPolicy, CircuitBreaker);

    public PaynowGatewayWrapper(ILogger<PaynowGatewayWrapper> logger)
    {
        _logger = logger;
    }

    public async Task<PaynowStatusResult> PollTransactionAsync(
        MerchantIntegration integration, string pollUrl, CancellationToken ct)
    {
        return await Resilience.ExecuteAsync(async (token) =>
        {
            token.ThrowIfCancellationRequested();
            var paynow = CreateClient(integration);
            var response = await paynow.PollTransactionAsync(pollUrl);

            var status = response.WasPaid ? TransactionStatus.Paid : TransactionStatus.Pending;

            return new PaynowStatusResult(
                Reference: response.Reference ?? string.Empty,
                Amount: response.Amount,
                Status: status,
                PaidOn: response.WasPaid ? DateTime.UtcNow : null,
                PaynowReference: response.Reference ?? string.Empty,
                RawResponse: JsonSerializer.Serialize(new
                {
                    response.Reference,
                    response.Amount,
                    response.WasPaid
                }));
        }, ct);
    }

    public async Task<PaynowInitResult> InitiatePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string customerEmail, CancellationToken ct)
    {
        return await Resilience.ExecuteAsync(async (token) =>
        {
            token.ThrowIfCancellationRequested();
            var paynow = CreateClient(integration);
            var payment = paynow.CreatePayment(reference, customerEmail);
            payment.Add("Payment", amount);

            var response = await paynow.SendAsync(payment);

            return new PaynowInitResult(
                Success: response.Success(),
                PollUrl: response.PollUrl(),
                RedirectUrl: response.RedirectLink(),
                Instructions: null,
                Error: response.Errors());
        }, ct);
    }

    public async Task<PaynowInitResult> InitiateMobilePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string phone, PaymentMethod method, CancellationToken ct)
    {
        return await Resilience.ExecuteAsync(async (token) =>
        {
            token.ThrowIfCancellationRequested();
            var paynow = CreateClient(integration);
            var payment = paynow.CreatePayment(reference);
            payment.Add("Payment", amount);

            var methodString = method switch
            {
                PaymentMethod.EcoCash => "ecocash",
                PaymentMethod.OneMoney => "onemoney",
                PaymentMethod.InnBucks => "innbucks",
                PaymentMethod.Telecash => "telecash",
                _ => throw new InvalidOperationException($"Mobile method {method} not supported")
            };

            var response = await paynow.SendMobileAsync(payment, phone, methodString);

            return new PaynowInitResult(
                Success: response.Success(),
                PollUrl: response.PollUrl(),
                RedirectUrl: null,
                Instructions: null,
                Error: response.Errors());
        }, ct);
    }

    private static Webdev.Payments.Paynow CreateClient(MerchantIntegration integration)
    {
        var paynow = new Webdev.Payments.Paynow(
            integration.PaynowIntegrationId,
            integration.PaynowIntegrationKey,
            integration.ResultUrl);
        paynow.ReturnUrl = integration.ReturnUrl;
        return paynow;
    }
}
