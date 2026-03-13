using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.TransactionIntelligence;

public class TransactionIntelligenceAgent : AgentBase
{
    public override string Name => "TransactionIntelligenceAgent";
    public override AgentType Type => AgentType.TransactionIntelligence;

    private readonly ReconciliationEngine _engine = new();
    private readonly AnomalyDetector _anomalyDetector = new();

    public TransactionIntelligenceAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<TransactionIntelligenceAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(15))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var txRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var reconRepo = scope.ServiceProvider.GetRequiredService<IReconciliationRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<ReconciliationInput>(task.InputPayload ?? "{}");
        var merchantId = input?.MerchantId ?? Guid.Empty;
        var periodStart = input?.PeriodStart ?? DateTime.UtcNow.AddDays(-1);
        var periodEnd = input?.PeriodEnd ?? DateTime.UtcNow;

        // Fetch transactions from all three sources
        var allTransactions = await txRepo.GetByMerchantAsync(merchantId, periodStart, periodEnd, ct);

        var paynowRecords = allTransactions.Where(t => t.Source == SourceType.Paynow).ToList();
        var merchantRecords = allTransactions.Where(t => t.Source == SourceType.Merchant).ToList();
        var bankRecords = allTransactions.Where(t => t.Source == SourceType.Bank).ToList();

        // Run reconciliation
        var report = _engine.Reconcile(
            merchantId, paynowRecords, merchantRecords, bankRecords, periodStart, periodEnd);

        // Check for velocity anomalies
        var velocityAnomalies = _anomalyDetector.DetectVelocityAnomalies(merchantId, allTransactions);
        var duplicateAnomalies = _anomalyDetector.DetectDuplicates(allTransactions);

        // Persist report
        await reconRepo.AddAsync(report, ct);

        // Publish events
        await eventBus.PublishAsync(
            new ReconciliationCompleted(report.Id, merchantId, report.AnomalyCount), ct);

        foreach (var match in report.Matches)
        {
            foreach (var anomaly in match.Anomalies)
            {
                await eventBus.PublishAsync(
                    new AnomalyDetected(anomaly.Id, merchantId, anomaly.Type, anomaly.Severity), ct);
            }
        }

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = true,
            OutputPayload = JsonSerializer.Serialize(new
            {
                reportId = report.Id,
                totalTransactions = report.TotalTransactions,
                matched = report.MatchedCount,
                unmatched = report.UnmatchedCount,
                anomalies = report.AnomalyCount,
                velocityAnomalies = velocityAnomalies.Count,
                duplicates = duplicateAnomalies.Count,
                matchRate = report.TotalTransactions > 0
                    ? (decimal)report.MatchedCount / report.TotalTransactions * 100
                    : 100m
            }),
            CompletedAt = DateTime.UtcNow
        };
    }
}

public record ReconciliationInput(Guid MerchantId, DateTime? PeriodStart, DateTime? PeriodEnd);
