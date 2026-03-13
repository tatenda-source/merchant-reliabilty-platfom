using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class SettlementRepository : ISettlementRepository
{
    private readonly MrpDbContext _db;

    public SettlementRepository(MrpDbContext db) => _db = db;

    public async Task<Settlement> AddAsync(Settlement settlement, CancellationToken ct)
    {
        _db.Settlements.Add(settlement);
        await _db.SaveChangesAsync(ct);
        return settlement;
    }

    public async Task<List<Settlement>> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        => await _db.Settlements
            .Where(s => s.MerchantId == merchantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<Settlement>> GetHighRiskAsync(decimal threshold, CancellationToken ct)
        => await _db.Settlements
            .Where(s => s.RiskScore >= threshold && s.ActualSettlementTime == null)
            .OrderByDescending(s => s.RiskScore)
            .ToListAsync(ct);

    public async Task UpdateAsync(Settlement settlement, CancellationToken ct)
    {
        _db.Settlements.Update(settlement);
        await _db.SaveChangesAsync(ct);
    }
}
