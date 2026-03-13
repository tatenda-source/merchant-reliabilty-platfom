using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class ReconciliationRepository : IReconciliationRepository
{
    private readonly MrpDbContext _db;

    public ReconciliationRepository(MrpDbContext db) => _db = db;

    public async Task<ReconciliationReport?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.ReconciliationReports
            .Include(r => r.Matches)
                .ThenInclude(m => m.Anomalies)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

    public async Task<List<ReconciliationReport>> GetByMerchantAsync(
        Guid merchantId, CancellationToken ct) =>
        await _db.ReconciliationReports
            .Where(r => r.MerchantId == merchantId)
            .OrderByDescending(r => r.GeneratedAt)
            .ToListAsync(ct);

    public async Task<ReconciliationReport> AddAsync(
        ReconciliationReport report, CancellationToken ct)
    {
        _db.ReconciliationReports.Add(report);
        await _db.SaveChangesAsync(ct);
        return report;
    }
}
