using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Infrastructure.Persistence.Repositories;

public class TransactionRepository : ITransactionRepository
{
    private readonly MrpDbContext _db;

    public TransactionRepository(MrpDbContext db) => _db = db;

    public async Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct) =>
        await _db.Transactions.FirstOrDefaultAsync(t => t.Id == id, ct);

    public async Task<List<Transaction>> GetByMerchantAsync(
        Guid merchantId, DateTime from, DateTime to, CancellationToken ct) =>
        await _db.Transactions
            .Where(t => t.MerchantId == merchantId && t.CreatedAt >= from && t.CreatedAt <= to)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

    public async Task<List<Transaction>> GetPendingAsync(SourceType source, CancellationToken ct) =>
        await _db.Transactions
            .Where(t => t.Source == source && t.Status == TransactionStatus.Pending)
            .Where(t => t.CreatedAt >= DateTime.UtcNow.AddHours(-72))
            .ToListAsync(ct);

    public async Task<Transaction> AddAsync(Transaction transaction, CancellationToken ct)
    {
        _db.Transactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
        return transaction;
    }

    public async Task AddRangeAsync(IEnumerable<Transaction> transactions, CancellationToken ct)
    {
        _db.Transactions.AddRange(transactions);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Transaction transaction, CancellationToken ct)
    {
        _db.Transactions.Update(transaction);
        await _db.SaveChangesAsync(ct);
    }
}
