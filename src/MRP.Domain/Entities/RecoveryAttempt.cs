using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class RecoveryAttempt
{
    public Guid Id { get; set; }
    public Guid AnomalyId { get; set; }
    public RecoveryStrategy Strategy { get; set; }
    public int AttemptNumber { get; set; }
    public bool IsSuccessful { get; set; }
    public string? ResultDetails { get; set; }
    public decimal ConfidenceScore { get; set; }
    public string? DecisionReason { get; set; }
    public DateTime AttemptedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public Anomaly Anomaly { get; set; } = null!;
}
