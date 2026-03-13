namespace MRP.Domain.Entities;

public class ReconciliationReport
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime GeneratedAt { get; set; }
    public int TotalTransactions { get; set; }
    public int MatchedCount { get; set; }
    public int UnmatchedCount { get; set; }
    public int AnomalyCount { get; set; }
    public decimal TotalVolume { get; set; }
    public decimal MatchedVolume { get; set; }
    public decimal DiscrepancyVolume { get; set; }

    public Merchant Merchant { get; set; } = null!;
    public ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();
}
