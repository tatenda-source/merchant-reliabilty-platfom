using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class RecoveryRepository : IRecoveryRepository
{
    private readonly MrpDbContext _db;

    public RecoveryRepository(MrpDbContext db) => _db = db;

    public async Task<List<Anomaly>> GetUnresolvedAnomaliesAsync(int page, int pageSize, CancellationToken ct) =>
        await _db.Anomalies
            .Where(a => !a.IsResolved)
            .OrderByDescending(a => a.Severity == "critical")
            .ThenByDescending(a => a.Severity == "high")
            .ThenByDescending(a => a.DetectedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<object> GetUnresolvedStatsAsync(CancellationToken ct)
    {
        var stats = await _db.Anomalies
            .Where(a => !a.IsResolved)
            .GroupBy(a => a.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var total = stats.Sum(s => s.Count);
        return new
        {
            queueSize = total,
            criticalCount = stats.FirstOrDefault(s => s.Severity == "critical")?.Count ?? 0,
            highCount = stats.FirstOrDefault(s => s.Severity == "high")?.Count ?? 0,
            mediumCount = stats.FirstOrDefault(s => s.Severity == "medium")?.Count ?? 0,
            lowCount = stats.FirstOrDefault(s => s.Severity == "low")?.Count ?? 0
        };
    }

    public async Task<Anomaly?> GetAnomalyByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Anomalies
            .Include(a => a.Merchant)
            .Include(a => a.RecoveryAttempts)
            .FirstOrDefaultAsync(a => a.Id == id, ct);

    public async Task<RecoveryAttempt> AddAttemptAsync(
        RecoveryAttempt attempt, CancellationToken ct)
    {
        _db.RecoveryAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
        return attempt;
    }

    public async Task<List<RecoveryAttempt>> GetAttemptsByAnomalyAsync(
        Guid anomalyId, CancellationToken ct) =>
        await _db.RecoveryAttempts
            .Where(r => r.AnomalyId == anomalyId)
            .OrderByDescending(r => r.AttemptedAt)
            .ToListAsync(ct);

    public async Task AddAnomalyAsync(Anomaly anomaly, CancellationToken ct)
    {
        _db.Anomalies.Add(anomaly);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAnomalyAsync(Anomaly anomaly, CancellationToken ct)
    {
        _db.Anomalies.Update(anomaly);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<Dictionary<RecoveryStrategy, decimal>> GetStrategySuccessRatesAsync(
        AnomalyType anomalyType, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var attempts = await _db.RecoveryAttempts
            .Where(r => r.Anomaly.Type == anomalyType && r.AttemptedAt >= cutoff)
            .GroupBy(r => r.Strategy)
            .Select(g => new
            {
                Strategy = g.Key,
                Total = g.Count(),
                Successful = g.Count(r => r.IsSuccessful)
            })
            .ToListAsync(ct);

        return attempts
            .Where(a => a.Total >= 3)
            .ToDictionary(
                a => a.Strategy,
                a => (decimal)a.Successful / a.Total * 100);
    }
}
