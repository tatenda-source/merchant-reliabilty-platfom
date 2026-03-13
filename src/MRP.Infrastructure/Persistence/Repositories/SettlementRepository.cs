using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class SettlementRepository : ISettlementRepository
{
    private readonly MrpDbContext _db;

    public SettlementRepository(MrpDbContext db) => _db = db;

    public async Task<SettlementPrediction> AddAsync(SettlementPrediction prediction, CancellationToken ct)
    {
        _db.SettlementPredictions.Add(prediction);
        await _db.SaveChangesAsync(ct);
        return prediction;
    }

    public async Task<List<SettlementPrediction>> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        => await _db.SettlementPredictions
            .Where(s => s.MerchantId == merchantId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<SettlementPrediction>> GetHighRiskAsync(decimal threshold, CancellationToken ct)
        => await _db.SettlementPredictions
            .Where(s => s.RiskScore >= threshold && s.ActualSettlementTime == null)
            .OrderByDescending(s => s.RiskScore)
            .ToListAsync(ct);

    public async Task UpdateAsync(SettlementPrediction prediction, CancellationToken ct)
    {
        _db.SettlementPredictions.Update(prediction);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<decimal> GetAverageSettlementHoursAsync(Guid merchantId, PaymentMethod method, CancellationToken ct)
    {
        var transactions = await _db.Transactions
            .Where(t => t.MerchantId == merchantId
                && t.PaymentMethod == method
                && t.PaidAt != null
                && t.SettledAt != null)
            .Select(t => new { t.PaidAt, t.SettledAt })
            .ToListAsync(ct);

        if (transactions.Count == 0) return 0m;

        var avgHours = transactions
            .Average(t => (t.SettledAt!.Value - t.PaidAt!.Value).TotalHours);

        return (decimal)avgHours;
    }
}
