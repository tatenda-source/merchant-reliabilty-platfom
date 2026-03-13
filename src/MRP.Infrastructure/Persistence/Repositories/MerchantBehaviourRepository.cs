using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class MerchantBehaviourRepository : IMerchantBehaviourRepository
{
    private readonly MrpDbContext _db;

    public MerchantBehaviourRepository(MrpDbContext db) => _db = db;

    public async Task<MerchantBehaviourProfile?> GetByMerchantAsync(Guid merchantId, CancellationToken ct)
        => await _db.MerchantBehaviourProfiles
            .FirstOrDefaultAsync(m => m.MerchantId == merchantId, ct);

    public async Task<MerchantBehaviourProfile> AddAsync(MerchantBehaviourProfile profile, CancellationToken ct)
    {
        _db.MerchantBehaviourProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    public async Task UpdateAsync(MerchantBehaviourProfile profile, CancellationToken ct)
    {
        _db.MerchantBehaviourProfiles.Update(profile);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<List<MerchantBehaviourProfile>> GetHighRiskProfilesAsync(decimal threshold, CancellationToken ct)
        => await _db.MerchantBehaviourProfiles
            .Where(m => m.RiskScore >= threshold)
            .OrderByDescending(m => m.RiskScore)
            .ToListAsync(ct);
}
