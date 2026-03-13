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
        var response = await Task.Run(() => paynow.PollTransaction(pollUrl), ct);

        return new PaynowStatusResult(
            Reference: response.Reference ?? string.Empty,
            Amount: response.Amount,
            Status: MapStatus(response.Status),
            PaidOn: response.PaidOn,
            PaynowReference: response.PaynowReference ?? string.Empty,
            RawResponse: JsonSerializer.Serialize(new
            {
                response.Reference,
                response.Amount,
                response.Status,
                response.PaidOn,
                response.PaynowReference
            }));
    }

    public async Task<PaynowInitResult> InitiatePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string customerEmail, CancellationToken ct)
    {
        var paynow = CreateClient(integration);
        var payment = paynow.CreatePayment(reference, customerEmail);
        payment.Add("Payment", amount);

        var response = await Task.Run(() => paynow.Send(payment), ct);

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

        var response = await Task.Run(
            () => paynow.SendMobile(payment, phone, methodString), ct);

        return new PaynowInitResult(
            Success: response.Success(),
            PollUrl: response.PollUrl(),
            RedirectUrl: null,
            Instructions: response.Instructions(),
            Error: response.Errors());
    }

    private static Paynow CreateClient(MerchantIntegration integration)
    {
        var paynow = new Paynow(integration.PaynowIntegrationId, integration.PaynowIntegrationKey);
        paynow.ResultUrl = integration.ResultUrl;
        paynow.ReturnUrl = integration.ReturnUrl;
        return paynow;
    }

    private static TransactionStatus MapStatus(string? status) => status?.ToLower() switch
    {
        "paid" => TransactionStatus.Paid,
        "awaiting delivery" => TransactionStatus.AwaitingDelivery,
        "delivered" => TransactionStatus.Delivered,
        "cancelled" => TransactionStatus.Cancelled,
        "refunded" => TransactionStatus.Refunded,
        "disputed" => TransactionStatus.Disputed,
        "failed" => TransactionStatus.Failed,
        _ => TransactionStatus.Pending
    };
}
