using FluentAssertions;
using MRP.Agents.Intelligence;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using Xunit;

namespace MRP.Tests.Unit.Agents.Intelligence;

public class AnomalyDetectorTests
{
    private readonly AnomalyDetector _sut = new();
    private readonly Guid _merchantId = Guid.NewGuid();

    [Fact]
    public void DetectVelocityAnomalies_WhenSpike_ShouldFlagAnomaly()
    {
        var transactions = Enumerable.Range(0, 15).Select(i => new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = _merchantId,
            CreatedAt = DateTime.UtcNow.AddSeconds(-i * 3),
            Amount = 10m,
            Source = SourceType.Paynow,
            Status = TransactionStatus.Paid
        }).ToList();

        var result = _sut.DetectVelocityAnomalies(_merchantId, transactions, maxPerMinute: 10);

        result.Should().NotBeEmpty();
        result.Should().Contain(a => a.Type == AnomalyType.VelocityAnomaly);
    }

    [Fact]
    public void DetectVelocityAnomalies_WhenNormal_ShouldReturnEmpty()
    {
        var transactions = Enumerable.Range(0, 5).Select(i => new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = _merchantId,
            CreatedAt = DateTime.UtcNow.AddMinutes(-i * 5),
            Amount = 10m,
            Source = SourceType.Paynow,
            Status = TransactionStatus.Paid
        }).ToList();

        var result = _sut.DetectVelocityAnomalies(_merchantId, transactions);

        result.Should().BeEmpty();
    }

    [Fact]
    public void DetectDuplicates_WhenDuplicateWithin5Minutes_ShouldFlag()
    {
        var transactions = new List<Transaction>
        {
            new() { Id = Guid.NewGuid(), MerchantReference = "INV-001", Amount = 50m,
                     PaymentMethod = PaymentMethod.EcoCash, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), MerchantReference = "INV-001", Amount = 50m,
                     PaymentMethod = PaymentMethod.EcoCash, CreatedAt = DateTime.UtcNow.AddMinutes(2) }
        };

        var result = _sut.DetectDuplicates(transactions);

        result.Should().ContainSingle();
        result.First().Type.Should().Be(AnomalyType.DuplicateTransaction);
    }

    [Fact]
    public void DetectDuplicates_WhenSameRefDifferentAmount_ShouldNotFlag()
    {
        var transactions = new List<Transaction>
        {
            new() { Id = Guid.NewGuid(), MerchantReference = "INV-002", Amount = 50m,
                     PaymentMethod = PaymentMethod.EcoCash, CreatedAt = DateTime.UtcNow },
            new() { Id = Guid.NewGuid(), MerchantReference = "INV-002", Amount = 75m,
                     PaymentMethod = PaymentMethod.EcoCash, CreatedAt = DateTime.UtcNow.AddMinutes(2) }
        };

        var result = _sut.DetectDuplicates(transactions);

        result.Should().BeEmpty();
    }
}
