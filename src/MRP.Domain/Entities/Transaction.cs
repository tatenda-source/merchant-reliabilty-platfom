using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class Transaction
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public string PaynowReference { get; set; } = string.Empty;
    public string MerchantReference { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public TransactionStatus Status { get; set; }
    public SourceType Source { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? CustomerPhone { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? PaidAt { get; set; }
    public DateTime? SettledAt { get; set; }
    public string? RawPayload { get; set; }

    public Merchant Merchant { get; set; } = null!;
}
