using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Agents.TransactionIntelligence;

public class ReconciliationEngine
{
    public ReconciliationReport Reconcile(
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

        // Index by reference for matching
        var paynowByRef = paynowRecords
            .GroupBy(t => t.PaynowReference)
            .ToDictionary(g => g.Key, g => g.First());
        var merchantByRef = merchantRecords
            .GroupBy(t => t.MerchantReference)
            .ToDictionary(g => g.Key, g => g.First());
        var bankByRef = bankRecords
            .GroupBy(t => t.PaynowReference)
            .ToDictionary(g => g.Key, g => g.First());

        // Collect all unique references
        var allRefs = paynowByRef.Keys
            .Union(merchantByRef.Keys)
            .Union(bankByRef.Keys)
            .Distinct()
            .ToList();

        var matches = new List<TransactionMatch>();
        var anomalies = new List<Anomaly>();
        decimal matchedVolume = 0;
        decimal discrepancyVolume = 0;
        int matchedCount = 0;

        foreach (var reference in allRefs)
        {
            paynowByRef.TryGetValue(reference, out var pnTx);
            merchantByRef.TryGetValue(reference, out var mTx);
            bankByRef.TryGetValue(reference, out var bkTx);

            var match = new TransactionMatch
            {
                Id = Guid.NewGuid(),
                ReconciliationReportId = report.Id,
                Reference = reference,
                PaynowTransactionId = pnTx?.Id,
                MerchantTransactionId = mTx?.Id,
                BankTransactionId = bkTx?.Id
            };

            var matchAnomalies = DetectAnomalies(match, pnTx, mTx, bkTx);

            if (matchAnomalies.Count == 0)
            {
                match.IsBalanced = true;
                match.ResolutionStatus = "resolved";
                matchedCount++;
                matchedVolume += pnTx?.Amount ?? mTx?.Amount ?? bkTx?.Amount ?? 0;
            }
            else
            {
                match.IsBalanced = false;
                match.ResolutionStatus = "unresolved";
                foreach (var anomaly in matchAnomalies)
                {
                    anomaly.TransactionMatchId = match.Id;
                    match.Anomalies.Add(anomaly);
                    anomalies.Add(anomaly);
                }

                // Calculate discrepancy
                var amounts = new[] { pnTx?.Amount, mTx?.Amount, bkTx?.Amount }
                    .Where(a => a.HasValue).Select(a => a!.Value).ToList();
                if (amounts.Count >= 2)
                    discrepancyVolume += amounts.Max() - amounts.Min();
            }

            matches.Add(match);
        }

        report.TotalTransactions = allRefs.Count;
        report.MatchedCount = matchedCount;
        report.UnmatchedCount = allRefs.Count - matchedCount;
        report.AnomalyCount = anomalies.Count;
        report.TotalVolume = paynowRecords.Sum(t => t.Amount);
        report.MatchedVolume = matchedVolume;
        report.DiscrepancyVolume = discrepancyVolume;
        report.Matches = matches;

        return report;
    }

    private List<Anomaly> DetectAnomalies(
        TransactionMatch match,
        Transaction? paynow,
        Transaction? merchant,
        Transaction? bank)
    {
        var anomalies = new List<Anomaly>();
        var now = DateTime.UtcNow;

        // Missing record anomalies
        if (paynow is null && (merchant is not null || bank is not null))
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.MissingPaynowRecord,
                Description = $"No Paynow record found for reference {match.Reference}",
                Severity = "high",
                DetectedAt = now
            });
        }

        if (merchant is null && paynow is not null)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.MissingMerchantRecord,
                Description = $"No merchant record found for reference {match.Reference}",
                Severity = "medium",
                DetectedAt = now
            });
        }

        if (bank is null && paynow is not null
            && paynow.Status == TransactionStatus.Paid
            && paynow.PaidAt.HasValue
            && (now - paynow.PaidAt.Value).TotalHours > 48)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.MissingBankRecord,
                Description = $"No bank settlement after 48h for reference {match.Reference}",
                Severity = "high",
                DetectedAt = now
            });
        }

        // Amount discrepancy
        if (paynow is not null && merchant is not null
            && paynow.Amount != merchant.Amount)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.AmountDiscrepancy,
                Description = $"Amount mismatch: Paynow={paynow.Amount}, Merchant={merchant.Amount}",
                Severity = Math.Abs(paynow.Amount - merchant.Amount) > 10 ? "high" : "medium",
                DetectedAt = now
            });
        }

        // Status mismatch
        if (paynow is not null && merchant is not null
            && paynow.Status != merchant.Status
            && paynow.Status != TransactionStatus.Pending)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.StatusMismatch,
                Description = $"Status mismatch: Paynow={paynow.Status}, Merchant={merchant.Status}",
                Severity = "medium",
                DetectedAt = now
            });
        }

        // Settlement delay
        if (paynow is not null && bank is not null
            && paynow.PaidAt.HasValue && bank.SettledAt.HasValue
            && (bank.SettledAt.Value - paynow.PaidAt.Value).TotalHours > 48)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.SettlementDelay,
                Description = $"Settlement delayed by {(bank.SettledAt.Value - paynow.PaidAt.Value).TotalHours:F0}h",
                Severity = "medium",
                DetectedAt = now
            });
        }

        return anomalies;
    }
}
