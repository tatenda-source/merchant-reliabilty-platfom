using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class RecoveryRepository : IRecoveryRepository
{
    private readonly MrpDbContext _db;

    public RecoveryRepository(MrpDbContext db) => _db = db;

    public async Task<List<Anomaly>> GetUnresolvedAnomaliesAsync(CancellationToken ct) =>
        await _db.Anomalies
            .Where(a => !a.IsResolved)
            .OrderByDescending(a => a.Severity == "critical")
            .ThenByDescending(a => a.Severity == "high")
            .ThenByDescending(a => a.DetectedAt)
            .ToListAsync(ct);

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
}
