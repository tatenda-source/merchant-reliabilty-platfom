using System.Text.Json;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;
using Webdev.Payments;

namespace MRP.Infrastructure.Paynow;

public class PaynowGatewayWrapper : IPaynowGateway
{
    public async Task<PaynowStatusResult> PollTransactionAsync(
        MerchantIntegration integration, string pollUrl, CancellationToken ct)
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
    }

    public async Task<PaynowInitResult> InitiatePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string customerEmail, CancellationToken ct)
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
    }

    public async Task<PaynowInitResult> InitiateMobilePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string phone, PaymentMethod method, CancellationToken ct)
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
            _ => throw new ArgumentException($"Mobile method {method} not supported")
        };

        var response = await paynow.SendMobileAsync(payment, phone, methodString);

        return new PaynowInitResult(
            Success: response.Success(),
            PollUrl: response.PollUrl(),
            RedirectUrl: null,
            Instructions: null,
            Error: response.Errors());
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
