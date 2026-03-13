using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.MerchantBehaviour;

public class MerchantBehaviourAgent : AgentBase
{
    public override string Name => "MerchantBehaviourAgent";
    public override AgentType Type => AgentType.MerchantBehaviour;

    private const decimal VelocitySpikeThreshold = 3.0m; // 3x normal traffic
    private const decimal HighRetryRateThreshold = 15.0m; // >15% retries
    private const decimal HighDuplicateRateThreshold = 5.0m; // >5% duplicates
    private const decimal HighCallbackFailureThreshold = 10.0m; // >10% callback failures

    public MerchantBehaviourAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<MerchantBehaviourAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(5))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var txRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var merchantRepo = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var behaviourRepo = scope.ServiceProvider.GetRequiredService<IMerchantBehaviourRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<BehaviourAnalysisInput>(task.InputPayload ?? "{}");
        var merchantId = input?.MerchantId ?? Guid.Empty;

        var merchant = await merchantRepo.GetByIdAsync(merchantId, ct);
        if (merchant is null)
        {
            return new AgentResult
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                IsSuccess = false,
                ErrorMessage = $"Merchant {merchantId} not found",
                CompletedAt = DateTime.UtcNow
            };
        }

        // Analyse last hour of transactions
        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentTx = await txRepo.GetByMerchantAsync(merchantId, oneHourAgo, DateTime.UtcNow, ct);

        // Analyse last 24 hours for baseline
        var oneDayAgo = DateTime.UtcNow.AddDays(-1);
        var dailyTx = await txRepo.GetByMerchantAsync(merchantId, oneDayAgo, DateTime.UtcNow, ct);

        // Calculate behaviour metrics
        var txPerMinute = recentTx.Count > 0
            ? recentTx.Count / 60m
            : 0;
        var txPerHour = (decimal)recentTx.Count;
        var avgTxPerHour = dailyTx.Count / 24m;
        var peakTxPerHour = CalculatePeakHourly(dailyTx);

        // Detect retries (same reference, multiple attempts)
        var retryGroups = recentTx
            .Where(t => !string.IsNullOrEmpty(t.MerchantReference))
            .GroupBy(t => t.MerchantReference)
            .Where(g => g.Count() > 1)
            .ToList();
        var retryRate = recentTx.Count > 0
            ? retryGroups.Sum(g => g.Count() - 1) / (decimal)recentTx.Count * 100
            : 0;

        // Detect duplicates (same ref + amount within 5 min)
        var duplicateCount = DetectDuplicates(recentTx);
        var duplicateRate = recentTx.Count > 0
            ? duplicateCount / (decimal)recentTx.Count * 100
            : 0;

        // Callback failure rate
        var callbackFailures = recentTx.Count(t => t.Status == TransactionStatus.Failed);
        var callbackFailureRate = recentTx.Count > 0
            ? callbackFailures / (decimal)recentTx.Count * 100
            : 0;

        // Calculate risk score
        var riskScore = CalculateRiskScore(
            txPerHour, avgTxPerHour, retryRate, duplicateRate, callbackFailureRate);

        // Build alerts
        var alerts = new List<string>();

        if (avgTxPerHour > 0 && txPerHour > avgTxPerHour * VelocitySpikeThreshold)
        {
            alerts.Add($"VelocitySpike: {txPerHour}/hr vs avg {avgTxPerHour:F1}/hr ({txPerHour / avgTxPerHour:F1}x normal)");
            await eventBus.PublishAsync(
                new MerchantBehaviourAlert(merchantId, "VelocitySpike", riskScore,
                    $"Traffic spike: {txPerHour}/hr vs normal {avgTxPerHour:F1}/hr"), ct);
        }

        if (retryRate > HighRetryRateThreshold)
        {
            alerts.Add($"HighRetryRate: {retryRate:F1}%");
            await eventBus.PublishAsync(
                new MerchantBehaviourAlert(merchantId, "HighRetryRate", riskScore,
                    $"Retry rate {retryRate:F1}% exceeds threshold"), ct);
        }

        if (duplicateRate > HighDuplicateRateThreshold)
        {
            alerts.Add($"DuplicateTransactions: {duplicateRate:F1}%");
            await eventBus.PublishAsync(
                new MerchantBehaviourAlert(merchantId, "DuplicateTransactions", riskScore,
                    $"Duplicate rate {duplicateRate:F1}% exceeds threshold"), ct);
        }

        if (callbackFailureRate > HighCallbackFailureThreshold)
        {
            alerts.Add($"CallbackInstability: {callbackFailureRate:F1}%");
            await eventBus.PublishAsync(
                new MerchantBehaviourAlert(merchantId, "CallbackInstability", riskScore,
                    $"Callback failure rate {callbackFailureRate:F1}% exceeds threshold"), ct);
        }

        // Update or create behaviour profile
        var profile = await behaviourRepo.GetByMerchantAsync(merchantId, ct);
        if (profile is null)
        {
            profile = new MerchantBehaviourProfile
            {
                Id = Guid.NewGuid(),
                MerchantId = merchantId,
                CreatedAt = DateTime.UtcNow
            };
        }

        profile.AvgTransactionsPerMinute = txPerMinute;
        profile.AvgTransactionsPerHour = avgTxPerHour;
        profile.RetryRate = retryRate;
        profile.DuplicateRate = duplicateRate;
        profile.CallbackFailureRate = callbackFailureRate;
        profile.RiskScore = riskScore;
        profile.PeakTransactionsPerHour = peakTxPerHour;
        profile.ActiveAlerts = alerts.Count > 0 ? JsonSerializer.Serialize(alerts) : null;
        profile.LastAnalysedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;

        if (profile.CreatedAt == profile.UpdatedAt)
            await behaviourRepo.AddAsync(profile, ct);
        else
            await behaviourRepo.UpdateAsync(profile, ct);

        // Adjust merchant reliability based on behaviour
        if (riskScore > 70 && merchant.ReliabilityScore > 10)
        {
            var penalty = Math.Min(riskScore * 0.05m, 5);
            merchant.ReliabilityScore = Math.Max(0, merchant.ReliabilityScore - penalty);
            await merchantRepo.UpdateAsync(merchant, ct);
        }

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = true,
            OutputPayload = JsonSerializer.Serialize(new
            {
                merchantId,
                txPerMinute = Math.Round(txPerMinute, 2),
                txPerHour,
                avgTxPerHour = Math.Round(avgTxPerHour, 1),
                retryRate = Math.Round(retryRate, 1),
                duplicateRate = Math.Round(duplicateRate, 1),
                callbackFailureRate = Math.Round(callbackFailureRate, 1),
                riskScore = Math.Round(riskScore, 1),
                alertCount = alerts.Count,
                alerts
            }),
            CompletedAt = DateTime.UtcNow
        };
    }

    private static decimal CalculateRiskScore(
        decimal currentTxPerHour, decimal avgTxPerHour,
        decimal retryRate, decimal duplicateRate, decimal callbackFailureRate)
    {
        decimal risk = 0;

        // Velocity spike component
        if (avgTxPerHour > 0)
        {
            var velocityRatio = currentTxPerHour / avgTxPerHour;
            if (velocityRatio > VelocitySpikeThreshold)
                risk += Math.Min((velocityRatio - 1) * 10, 35);
        }

        // Retry rate component
        if (retryRate > HighRetryRateThreshold)
            risk += Math.Min(retryRate * 1.5m, 25);

        // Duplicate rate component
        if (duplicateRate > HighDuplicateRateThreshold)
            risk += Math.Min(duplicateRate * 3, 20);

        // Callback failure component
        if (callbackFailureRate > HighCallbackFailureThreshold)
            risk += Math.Min(callbackFailureRate * 2, 20);

        return Math.Clamp(risk, 0, 100);
    }

    private static decimal CalculatePeakHourly(List<Transaction> transactions)
    {
        if (transactions.Count == 0) return 0;

        return transactions
            .GroupBy(t => new { t.CreatedAt.Date, t.CreatedAt.Hour })
            .Max(g => (decimal)g.Count());
    }

    private static int DetectDuplicates(List<Transaction> transactions)
    {
        var duplicates = 0;
        var grouped = transactions
            .Where(t => !string.IsNullOrEmpty(t.MerchantReference))
            .GroupBy(t => new { t.MerchantReference, t.Amount });

        foreach (var group in grouped)
        {
            var ordered = group.OrderBy(t => t.CreatedAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
            {
                if ((ordered[i].CreatedAt - ordered[i - 1].CreatedAt).TotalMinutes <= 5)
                    duplicates++;
            }
        }

        return duplicates;
    }
}

public record BehaviourAnalysisInput(Guid MerchantId);
