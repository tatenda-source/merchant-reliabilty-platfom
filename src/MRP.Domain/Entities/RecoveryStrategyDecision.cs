using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class RecoveryStrategyDecision
{
    public Guid Id { get; set; }
    public Guid AnomalyId { get; set; }
    public Guid MerchantId { get; set; }
    public RecoveryStrategy ChosenStrategy { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string DecisionReason { get; set; } = string.Empty;
    public decimal MerchantReliabilityAtDecision { get; set; }
    public decimal FinancialRiskAmount { get; set; }
    public bool WasEffective { get; set; }
    public DateTime DecidedAt { get; set; }

    public Anomaly Anomaly { get; set; } = null!;
    public Merchant Merchant { get; set; } = null!;
}
