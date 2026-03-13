using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Domain.Interfaces;

public record PaynowStatusResult(
    string Reference,
    decimal Amount,
    TransactionStatus Status,
    DateTime? PaidOn,
    string PaynowReference,
    string RawResponse);

public record PaynowInitResult(
    bool Success,
    string? PollUrl,
    string? RedirectUrl,
    string? Instructions,
    string? Error);

public interface IPaynowGateway
{
    Task<PaynowStatusResult> PollTransactionAsync(
        MerchantIntegration integration, string pollUrl, CancellationToken ct);

    Task<PaynowInitResult> InitiatePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string customerEmail, CancellationToken ct);

    Task<PaynowInitResult> InitiateMobilePaymentAsync(
        MerchantIntegration integration, string reference, decimal amount,
        string phone, PaymentMethod method, CancellationToken ct);
}
