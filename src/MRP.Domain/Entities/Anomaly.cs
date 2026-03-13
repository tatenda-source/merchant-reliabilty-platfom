using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class Anomaly
{
    public Guid Id { get; set; }
    public Guid TransactionMatchId { get; set; }
    public AnomalyType Type { get; set; }
    public string Description { get; set; } = string.Empty;
    public string Severity { get; set; } = "medium";
    public bool IsResolved { get; set; }
    public DateTime DetectedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }

    public TransactionMatch Match { get; set; } = null!;
    public RecoveryAttempt? RecoveryAttempt { get; set; }
}
