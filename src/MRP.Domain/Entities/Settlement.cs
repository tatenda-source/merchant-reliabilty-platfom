using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class Settlement
{
    public Guid Id { get; set; }
    public Guid TransactionId { get; set; }
    public Guid MerchantId { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public decimal RiskScore { get; set; }
    public decimal Confidence { get; set; }
    public DateTime PredictedSettlementTime { get; set; }
    public DateTime? ActualSettlementTime { get; set; }
    public bool WasAccurate { get; set; }
    public string? RiskFactors { get; set; }
    public DateTime CreatedAt { get; set; }

    public Transaction Transaction { get; set; } = null!;
    public Merchant Merchant { get; set; } = null!;
}
