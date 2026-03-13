namespace MRP.Domain.Entities;

public class MerchantProfile
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public decimal AvgTransactionsPerHour { get; set; }
    public decimal PeakTransactionsPerHour { get; set; }
    public decimal RetryRate { get; set; }
    public decimal DuplicateRate { get; set; }
    public decimal CallbackFailureRate { get; set; }
    public decimal BehaviourRiskScore { get; set; }
    public string? ActiveAlerts { get; set; }
    public DateTime LastAnalysedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public Merchant Merchant { get; set; } = null!;
}
