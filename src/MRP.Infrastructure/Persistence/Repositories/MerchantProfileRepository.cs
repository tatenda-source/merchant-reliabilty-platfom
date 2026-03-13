using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class MerchantProfileRepository : IMerchantProfileRepository
{
    private readonly MrpDbContext _db;

    public MerchantProfileRepository(MrpDbContext db) => _db = db;

    public async Task<MerchantProfile?> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        => await _db.MerchantProfiles
            .FirstOrDefaultAsync(m => m.MerchantId == merchantId, ct);

    public async Task<MerchantProfile> AddAsync(MerchantProfile profile, CancellationToken ct)
    {
        _db.MerchantProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpdateAsync(MerchantProfile profile, CancellationToken ct)
    {
        _db.MerchantProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<MerchantProfile>> GetHighRiskAsync(decimal threshold, CancellationToken ct)
        => await _db.MerchantProfiles
            .Where(m => m.BehaviourRiskScore >= threshold)
            .OrderByDescending(m => m.BehaviourRiskScore)
            .ToListAsync(ct);
}
