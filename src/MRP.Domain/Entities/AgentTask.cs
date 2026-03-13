using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class AgentTask
{
    public Guid Id { get; set; }
    public AgentType AgentType { get; set; }
    public string TaskType { get; set; } = string.Empty;
    public string? InputPayload { get; set; }
    public string Status { get; set; } = "queued";
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public AgentResult? Result { get; set; }
}
