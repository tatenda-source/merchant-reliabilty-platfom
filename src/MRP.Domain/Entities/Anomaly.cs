using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class Anomaly
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Guid? TransactionId { get; set; }
    public Guid? ReconciliationReportId { get; set; }
    public AnomalyType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public decimal? Amount { get; set; }
    public bool IsResolved { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public Merchant Merchant { get; set; } = null!;
    public Transaction? Transaction { get; set; }
    public ReconciliationReport? Report { get; set; }
    public ICollection<RecoveryAttempt> RecoveryAttempts { get; set; } = new List<RecoveryAttempt>();
}
