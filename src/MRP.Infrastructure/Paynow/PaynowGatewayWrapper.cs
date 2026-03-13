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

public class PaynowGatewayWrapper : IPaynowGateway
{
    private readonly ILogger<PaynowGatewayWrapper> _logger;

    // Retry: 3 attempts with exponential backoff (1s, 2s, 4s)
    private readonly AsyncRetryPolicy _retryPolicy;

    // Circuit breaker: open after 5 failures in 30s, stay open 60s
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    // Combined pipeline
    private readonly AsyncPolicy _resilience;

    public PaynowGatewayWrapper(ILogger<PaynowGatewayWrapper> logger)
    {
        _logger = logger;

        _retryPolicy = Policy
            .Handle<Exception>(ex => ex is not InvalidOperationException)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (ex, delay, attempt, _) =>
                    _logger.LogWarning("Paynow retry #{Attempt} after {Delay}ms: {Message}",
                        attempt, delay.TotalMilliseconds, ex.Message));

        _circuitBreaker = Policy
            .Handle<Exception>(ex => ex is not InvalidOperationException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(60),
                onBreak: (ex, duration) =>
                    _logger.LogError("Paynow circuit OPEN for {Duration}s: {Message}",
                        duration.TotalSeconds, ex.Message),
                onReset: () =>
                    _logger.LogInformation("Paynow circuit CLOSED — recovered"),
                onHalfOpen: () =>
                    _logger.LogInformation("Paynow circuit HALF-OPEN — testing"));

        _resilience = Policy.WrapAsync(_retryPolicy, _circuitBreaker);
    }

    public async Task<PaynowStatusResult> PollTransactionAsync(
        MerchantIntegration integration, string pollUrl, CancellationToken ct)
    {
        return await _resilience.ExecuteAsync(async () =>
        {
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
        });
    }

    public async Task<PaynowInitResult> InitiatePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string customerEmail, CancellationToken ct)
    {
        return await _resilience.ExecuteAsync(async () =>
        {
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
        });
    }

    public async Task<PaynowInitResult> InitiateMobilePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string phone, PaymentMethod method, CancellationToken ct)
    {
        return await _resilience.ExecuteAsync(async () =>
        {
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
        });
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
