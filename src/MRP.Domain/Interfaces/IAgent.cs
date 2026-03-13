using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Domain.Interfaces;

public interface IAgent
{
    string Name { get; }
    AgentType Type { get; }
    Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct);
    Task<bool> HealthCheckAsync(CancellationToken ct);
}
