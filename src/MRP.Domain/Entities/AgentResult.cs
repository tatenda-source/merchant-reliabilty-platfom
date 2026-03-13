namespace MRP.Domain.Entities;

public class AgentResult
{
    public Guid Id { get; set; }
    public Guid AgentTaskId { get; set; }
    public bool IsSuccess { get; set; }
    public string? OutputPayload { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
    public DateTime CompletedAt { get; set; }

    public AgentTask Task { get; set; } = null!;
}
