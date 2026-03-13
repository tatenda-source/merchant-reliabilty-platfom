using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Tests.Unit.Fixtures;

public static class TransactionFixtures
{
    private static int _counter;

    public static Transaction CreatePaynow(
        string reference, decimal amount, TransactionStatus status = TransactionStatus.Paid,
        PaymentMethod method = PaymentMethod.EcoCash, Guid? merchantId = null,
        DateTime? createdAt = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId ?? Guid.NewGuid(),
            PaynowReference = reference,
            MerchantReference = reference,
            Amount = amount,
            Currency = "USD",
            Status = status,
            Source = SourceType.Paynow,
            PaymentMethod = method,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            PaidAt = status == TransactionStatus.Paid ? DateTime.UtcNow : null
        };
    }

    public static Transaction CreateMerchant(
        string reference, decimal amount, TransactionStatus status = TransactionStatus.Paid,
        Guid? merchantId = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId ?? Guid.NewGuid(),
            PaynowReference = string.Empty,
            MerchantReference = reference,
            Amount = amount,
            Currency = "USD",
            Status = status,
            Source = SourceType.Merchant,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Transaction CreateBank(
        string reference, decimal amount, Guid? merchantId = null,
        DateTime? settledAt = null)
    {
        return new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId ?? Guid.NewGuid(),
            PaynowReference = reference,
            MerchantReference = reference,
            Amount = amount,
            Currency = "USD",
            Status = TransactionStatus.Paid,
            Source = SourceType.Bank,
            CreatedAt = DateTime.UtcNow,
            SettledAt = settledAt ?? DateTime.UtcNow
        };
    }

    public static List<Transaction> CreateBatch(
        SourceType source, int count, Guid merchantId, decimal avgAmount = 50m)
    {
        var rng = new Random(42);
        return Enumerable.Range(0, count).Select(i =>
        {
            var tx = new Transaction
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                PaynowReference = $"REF-{Interlocked.Increment(ref _counter)}",
                MerchantReference = $"REF-{_counter}",
                Amount = avgAmount + (decimal)(rng.NextDouble() * 20 - 10),
                Currency = "USD",
                Status = TransactionStatus.Paid,
                Source = source,
                PaymentMethod = PaymentMethod.EcoCash,
                CreatedAt = DateTime.UtcNow.AddHours(-rng.Next(1, 720))
            };
            return tx;
        }).ToList();
    }
}
