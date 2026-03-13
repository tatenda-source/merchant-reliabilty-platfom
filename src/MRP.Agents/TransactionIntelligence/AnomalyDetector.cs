using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Agents.TransactionIntelligence;

public class AnomalyDetector
{
    public List<Anomaly> DetectVelocityAnomalies(
        Guid merchantId, List<Transaction> recentTransactions,
        int maxPerMinute = 10, int maxPerHour = 100)
    {
        var anomalies = new List<Anomaly>();
        var now = DateTime.UtcNow;

        var lastMinute = recentTransactions
            .Count(t => t.CreatedAt >= now.AddMinutes(-1));
        var lastHour = recentTransactions
            .Count(t => t.CreatedAt >= now.AddHours(-1));

        if (lastMinute > maxPerMinute)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.VelocityAnomaly,
                Description = $"Velocity spike: {lastMinute} transactions in last minute (threshold: {maxPerMinute})",
                Severity = "high",
                DetectedAt = now
            });
        }

        if (lastHour > maxPerHour)
        {
            anomalies.Add(new Anomaly
            {
                Id = Guid.NewGuid(),
                Type = AnomalyType.VelocityAnomaly,
                Description = $"Hourly volume anomaly: {lastHour} transactions (threshold: {maxPerHour})",
                Severity = "medium",
                DetectedAt = now
            });
        }

        return anomalies;
    }

    public List<Anomaly> DetectDuplicates(List<Transaction> transactions)
    {
        var anomalies = new List<Anomaly>();
        var seen = new Dictionary<string, Transaction>();

        foreach (var tx in transactions.OrderBy(t => t.CreatedAt))
        {
            var key = $"{tx.MerchantReference}_{tx.Amount}_{tx.PaymentMethod}";

            if (seen.TryGetValue(key, out var existing)
                && (tx.CreatedAt - existing.CreatedAt).TotalMinutes < 5)
            {
                anomalies.Add(new Anomaly
                {
                    Id = Guid.NewGuid(),
                    Type = AnomalyType.DuplicateTransaction,
                    Description = $"Possible duplicate: {tx.MerchantReference} ${tx.Amount} within 5 minutes",
                    Severity = "high",
                    DetectedAt = DateTime.UtcNow
                });
            }

            seen[key] = tx;
        }

        return anomalies;
    }
}
