using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class AgentTaskRepository : IAgentTaskRepository
{
    private readonly MrpDbContext _db;

    public AgentTaskRepository(MrpDbContext db) => _db = db;

    public async Task<AgentTask?> DequeueAsync(AgentType agentType, CancellationToken ct) =>
        await _db.AgentTasks
            .Where(t => t.AgentType == agentType && t.Status == "queued")
            .OrderBy(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);

    public async Task<AgentTask> AddAsync(AgentTask task, CancellationToken ct)
    {
        _db.AgentTasks.Add(task);
        await _db.SaveChangesAsync(ct);
        return task;
    }

    public async Task UpdateAsync(AgentTask task, CancellationToken ct)
    {
        _db.AgentTasks.Update(task);
        await _db.SaveChangesAsync(ct);
    }

    public async Task SaveResultAsync(AgentResult result, CancellationToken ct)
    {
        _db.AgentResults.Add(result);
        await _db.SaveChangesAsync(ct);
    }
}
