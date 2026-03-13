using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class MerchantRepository : IMerchantRepository
{
    private readonly MrpDbContext _db;

    public MerchantRepository(MrpDbContext db) => _db = db;

    public async Task<Merchant?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Merchants
            .Include(m => m.Integration)
            .FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<List<Merchant>> GetAllAsync(int page, int pageSize, CancellationToken ct) =>
        await _db.Merchants
            .Include(m => m.Integration)
            .OrderByDescending(m => m.OnboardedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

    public async Task<Merchant> AddAsync(Merchant merchant, CancellationToken ct)
    {
        _db.Merchants.Add(merchant);
        await _db.SaveChangesAsync(ct);
        return merchant;
    }

    public async Task UpdateAsync(Merchant merchant, CancellationToken ct)
    {
        _db.Merchants.Update(merchant);
        await _db.SaveChangesAsync(ct);
    }
}
