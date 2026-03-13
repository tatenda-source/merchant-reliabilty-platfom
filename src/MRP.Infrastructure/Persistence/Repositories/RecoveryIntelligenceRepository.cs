using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class RecoveryIntelligenceRepository : IRecoveryIntelligenceRepository
{
    private readonly MrpDbContext _db;

    public RecoveryIntelligenceRepository(MrpDbContext db) => _db = db;

    public async Task<RecoveryStrategyDecision> AddAsync(RecoveryStrategyDecision decision, CancellationToken ct)
    {
        _db.RecoveryStrategyDecisions.Add(decision);
        await _db.SaveChangesAsync(ct);
        return decision;
    }

    public async Task<List<RecoveryStrategyDecision>> GetByAnomalyAsync(Guid anomalyId, CancellationToken ct)
        => await _db.RecoveryStrategyDecisions
            .Where(r => r.AnomalyId == anomalyId)
            .OrderByDescending(r => r.DecidedAt)
            .ToListAsync(ct);

    public async Task UpdateAsync(RecoveryStrategyDecision decision, CancellationToken ct)
    {
        _db.RecoveryStrategyDecisions.Update(decision);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetStrategySuccessRateAsync(RecoveryStrategy strategy, CancellationToken ct)
    {
        var decisions = await _db.RecoveryStrategyDecisions
            .Where(r => r.ChosenStrategy == strategy)
            .ToListAsync(ct);

        if (decisions.Count == 0) return 0m;

        var successful = decisions.Count(r => r.WasEffective);
        return (decimal)successful / decisions.Count * 100;
    }
}
