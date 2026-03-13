using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Agents.Intelligence;

public class ReconciliationEngine
{
    public ReconciliationResult Reconcile(
        Guid merchantId,
        List<Transaction> paynowRecords,
        List<Transaction> merchantRecords,
        List<Transaction> bankRecords,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var report = new ReconciliationReport
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            GeneratedAt = DateTime.UtcNow
        };

        var paynowByRef = paynowRecords
            .GroupBy(t => t.PaynowReference)
            .ToDictionary(g => g.Key, g => g.First());
        var merchantByRef = merchantRecords
            .GroupBy(t => t.MerchantReference)
            .ToDictionary(g => g.Key, g => g.First());
        var bankByRef = bankRecords
            .GroupBy(t => t.PaynowReference)
            .ToDictionary(g => g.Key, g => g.First());

        var allRefs = paynowByRef.Keys
            .Union(merchantByRef.Keys)
            .Union(bankByRef.Keys)
            .Distinct()
            .ToList();

        var anomalies = new List<Anomaly>();
        decimal matchedVolume = 0;
        decimal discrepancyVolume = 0;
        int matchedCount = 0;

        foreach (var reference in allRefs)
        {
            paynowByRef.TryGetValue(reference, out var pnTx);
            merchantByRef.TryGetValue(reference, out var mTx);
            bankByRef.TryGetValue(reference, out var bkTx);

            var refAnomalies = DetectAnomalies(merchantId, report.Id, reference, pnTx, mTx, bkTx);

            if (refAnomalies.Count == 0)
            {
                matchedCount++;
                matchedVolume += pnTx?.Amount ?? mTx?.Amount ?? bkTx?.Amount ?? 0;
            }
            else
            {
                anomalies.AddRange(refAnomalies);
                var amounts = new[] { pnTx?.Amount, mTx?.Amount, bkTx?.Amount }
                    .Where(a => a.HasValue).Select(a => a!.Value).ToList();
                if (amounts.Count >= 2)
                    discrepancyVolume += amounts.Max() - amounts.Min();
            }
        }

        report.TotalTransactions = allRefs.Count;
        report.MatchedCount = matchedCount;
        report.UnmatchedCount = allRefs.Count - matchedCount;
        report.AnomalyCount = anomalies.Count;
        report.TotalVolume = paynowRecords.Sum(t => t.Amount);
        report.MatchedVolume = matchedVolume;
        report.DiscrepancyVolume = discrepancyVolume;
        report.Anomalies = anomalies;

        return new ReconciliationResult(report, anomalies);
    }

    private static List<Anomaly> DetectAnomalies(
        Guid merchantId, Guid reportId, string reference,
        Transaction? paynow, Transaction? merchant, Transaction? bank)
    {
        var anomalies = new List<Anomaly>();
        var now = DateTime.UtcNow;

        if (paynow is null && (merchant is not null || bank is not null))
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = merchant?.Id ?? bank?.Id,
                Type = AnomalyType.MissingPaynowRecord,
                Description = $"No Paynow record found for reference {reference}",
                Severity = "high", DetectedAt = now,
                Amount = merchant?.Amount ?? bank?.Amount
            });
        }

        if (merchant is null && paynow is not null)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = paynow.Id,
                Type = AnomalyType.MissingMerchantRecord,
                Description = $"No merchant record found for reference {reference}",
                Severity = "medium", DetectedAt = now,
                Amount = paynow.Amount
            });
        }

        if (bank is null && paynow is not null
            && paynow.Status == TransactionStatus.Paid
            && paynow.PaidAt.HasValue
            && (now - paynow.PaidAt.Value).TotalHours > 48)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = paynow.Id,
                Type = AnomalyType.MissingBankRecord,
                Description = $"No bank settlement after 48h for reference {reference}",
                Severity = "high", DetectedAt = now,
                Amount = paynow.Amount
            });
        }

        if (paynow is not null && merchant is not null && paynow.Amount != merchant.Amount)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = paynow.Id,
                Type = AnomalyType.AmountDiscrepancy,
                Description = $"Amount mismatch: Paynow={paynow.Amount}, Merchant={merchant.Amount}",
                Severity = Math.Abs(paynow.Amount - merchant.Amount) > 10 ? "high" : "medium",
                DetectedAt = now,
                Amount = Math.Abs(paynow.Amount - merchant.Amount)
            });
        }

        if (paynow is not null && merchant is not null
            && paynow.Status != merchant.Status
            && paynow.Status != TransactionStatus.Pending)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = paynow.Id,
                Type = AnomalyType.StatusMismatch,
                Description = $"Status mismatch: Paynow={paynow.Status}, Merchant={merchant.Status}",
                Severity = "medium", DetectedAt = now,
                Amount = paynow.Amount
            });
        }

        if (paynow is not null && bank is not null
            && paynow.PaidAt.HasValue && bank.SettledAt.HasValue
            && (bank.SettledAt.Value - paynow.PaidAt.Value).TotalHours > 48)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(), MerchantId = merchantId,
                ReconciliationReportId = reportId,
                TransactionId = paynow.Id,
                Type = AnomalyType.SettlementDelay,
                Description = $"Settlement delayed by {(bank.SettledAt.Value - paynow.PaidAt.Value).TotalHours:F0}h",
                Severity = "medium", DetectedAt = now,
                Amount = paynow.Amount
            });
        }

        return anomalies;
    }
}

public record ReconciliationResult(ReconciliationReport Report, List<Anomaly> Anomalies);
