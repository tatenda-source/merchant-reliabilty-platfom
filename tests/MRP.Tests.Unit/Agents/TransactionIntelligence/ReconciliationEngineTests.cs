using FluentAssertions;
using MRP.Agents.TransactionIntelligence;
using MRP.Domain.Enums;
using MRP.Tests.Unit.Fixtures;
using Xunit;

namespace MRP.Tests.Unit.Agents.TransactionIntelligence;

public class ReconciliationEngineTests
{
    private readonly ReconciliationEngine _sut = new();
    private readonly Guid _merchantId = Guid.NewGuid();

    [Fact]
    public void Reconcile_WhenAllThreeSourcesMatch_ShouldReturnFullyBalanced()
    {
        var pn = new[] { TransactionFixtures.CreatePaynow("REF-001", 50m) };
        var mx = new[] { TransactionFixtures.CreateMerchant("REF-001", 50m) };
        var bk = new[] { TransactionFixtures.CreateBank("REF-001", 50m) };

        var report = _sut.Reconcile(_merchantId, pn.ToList(), mx.ToList(), bk.ToList(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.MatchedCount.Should().Be(1);
        report.UnmatchedCount.Should().Be(0);
        report.AnomalyCount.Should().Be(0);
    }

    [Fact]
    public void Reconcile_WhenMerchantRecordMissing_ShouldDetectMissingMerchantAnomaly()
    {
        var pn = new[] { TransactionFixtures.CreatePaynow("REF-002", 75m) };

        var report = _sut.Reconcile(_merchantId, pn.ToList(), new(), new(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.UnmatchedCount.Should().Be(1);
        report.AnomalyCount.Should().BeGreaterThan(0);
        report.Matches.First().Anomalies
            .Should().Contain(a => a.Type == AnomalyType.MissingMerchantRecord);
    }

    [Fact]
    public void Reconcile_WhenAmountsDiffer_ShouldDetectAmountDiscrepancy()
    {
        var pn = new[] { TransactionFixtures.CreatePaynow("REF-003", 100m) };
        var mx = new[] { TransactionFixtures.CreateMerchant("REF-003", 95m) };

        var report = _sut.Reconcile(_merchantId, pn.ToList(), mx.ToList(), new(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.Matches.First().Anomalies
            .Should().Contain(a => a.Type == AnomalyType.AmountDiscrepancy);
        report.DiscrepancyVolume.Should().Be(5m);
    }

    [Fact]
    public void Reconcile_WhenStatusMismatch_ShouldDetectStatusAnomaly()
    {
        var pn = new[] { TransactionFixtures.CreatePaynow("REF-004", 50m, TransactionStatus.Paid) };
        var mx = new[] { TransactionFixtures.CreateMerchant("REF-004", 50m, TransactionStatus.Pending) };

        var report = _sut.Reconcile(_merchantId, pn.ToList(), mx.ToList(), new(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.Matches.First().Anomalies
            .Should().Contain(a => a.Type == AnomalyType.StatusMismatch);
    }

    [Fact]
    public void Reconcile_WhenNoTransactions_ShouldReturnEmptyReport()
    {
        var report = _sut.Reconcile(_merchantId, new(), new(), new(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.TotalTransactions.Should().Be(0);
        report.MatchedCount.Should().Be(0);
        report.AnomalyCount.Should().Be(0);
    }

    [Fact]
    public void Reconcile_WithMultipleTransactions_ShouldCalculateCorrectMatchRate()
    {
        var pn = new[]
        {
            TransactionFixtures.CreatePaynow("REF-A", 50m),
            TransactionFixtures.CreatePaynow("REF-B", 30m),
            TransactionFixtures.CreatePaynow("REF-C", 75m)
        };
        var mx = new[]
        {
            TransactionFixtures.CreateMerchant("REF-A", 50m),
            TransactionFixtures.CreateMerchant("REF-B", 30m)
            // REF-C missing from merchant
        };

        var report = _sut.Reconcile(_merchantId, pn.ToList(), mx.ToList(), new(),
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        report.TotalTransactions.Should().Be(3);
        report.MatchedCount.Should().Be(2);
        report.UnmatchedCount.Should().Be(1);
    }
}
