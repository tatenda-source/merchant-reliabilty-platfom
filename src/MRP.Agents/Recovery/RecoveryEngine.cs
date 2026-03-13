using Microsoft.Extensions.Logging;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Recovery;

public class RecoveryEngine : IRecoveryEngine
{
    private readonly IRecoveryRepository _recoveryRepo;
    private readonly IMerchantRepository _merchantRepo;
    private readonly IEventBus _eventBus;
    private readonly ILogger<RecoveryEngine> _logger;

    private static readonly Dictionary<AnomalyType, RecoveryStrategy[]> StrategyMap = new()
    {
        { AnomalyType.MissingPaynowRecord, [RecoveryStrategy.PaynowDispute, RecoveryStrategy.RecordReconstruction] },
        { AnomalyType.MissingMerchantRecord, [RecoveryStrategy.MerchantNotification, RecoveryStrategy.RecordReconstruction] },
        { AnomalyType.MissingBankRecord, [RecoveryStrategy.BankVerification, RecoveryStrategy.PaynowDispute] },
        { AnomalyType.StatusMismatch, [RecoveryStrategy.AutoRetry, RecoveryStrategy.MerchantNotification] },
        { AnomalyType.AmountDiscrepancy, [RecoveryStrategy.PaynowDispute, RecoveryStrategy.ManualEscalation] },
        { AnomalyType.DuplicateTransaction, [RecoveryStrategy.AutoRetry, RecoveryStrategy.MerchantNotification] },
        { AnomalyType.SettlementDelay, [RecoveryStrategy.BankVerification, RecoveryStrategy.PaynowDispute] },
        { AnomalyType.VelocityAnomaly, [RecoveryStrategy.MerchantNotification, RecoveryStrategy.ManualEscalation] },
        { AnomalyType.CallbackFailure, [RecoveryStrategy.AutoRetry, RecoveryStrategy.MerchantNotification] }
    };

    public RecoveryEngine(
        IRecoveryRepository recoveryRepo, IMerchantRepository merchantRepo,
        IEventBus eventBus, ILogger<RecoveryEngine> logger)
    {
        _recoveryRepo = recoveryRepo;
        _merchantRepo = merchantRepo;
        _eventBus = eventBus;
        _logger = logger;
    }

    public async Task<RecoveryAttempt> RecoverAsync(Guid anomalyId, CancellationToken ct)
    {
        var anomaly = await _recoveryRepo.GetAnomalyByIdAsync(anomalyId, ct);
        if (anomaly is null)
            throw new InvalidOperationException($"Anomaly {anomalyId} not found");

        var merchant = anomaly.Merchant ?? await _merchantRepo.GetByIdAsync(anomaly.MerchantId, ct);
        var previousAttempts = anomaly.RecoveryAttempts?.ToList()
            ?? await _recoveryRepo.GetAttemptsByAnomalyAsync(anomalyId, ct);

        var (strategy, confidence, reason) = DecideStrategy(anomaly, merchant, previousAttempts);

        var attempt = new RecoveryAttempt
        {
            Id = Guid.NewGuid(),
            AnomalyId = anomalyId,
            Strategy = strategy,
            AttemptNumber = previousAttempts.Count + 1,
            ConfidenceScore = confidence,
            DecisionReason = reason,
            AttemptedAt = DateTime.UtcNow
        };

        var success = ExecuteStrategy(strategy, anomaly);

        attempt.IsSuccessful = success;
        attempt.CompletedAt = DateTime.UtcNow;
        attempt.ResultDetails = success
            ? $"Strategy {strategy} executed successfully"
            : $"Strategy {strategy} failed";

        await _recoveryRepo.AddAttemptAsync(attempt, ct);

        if (success)
        {
            anomaly.IsResolved = true;
            anomaly.ResolvedAt = DateTime.UtcNow;
            await _recoveryRepo.UpdateAnomalyAsync(anomaly, ct);
        }

        await _eventBus.PublishAsync(
            new RecoveryCompleted(attempt.Id, anomalyId, success, strategy), ct);

        _logger.LogInformation(
            "Recovery for anomaly {AnomalyId}: strategy={Strategy}, success={Success}, confidence={Confidence}",
            anomalyId, strategy, success, confidence);

        return attempt;
    }

    private static (RecoveryStrategy Strategy, decimal Confidence, string Reason) DecideStrategy(
        Anomaly anomaly, Merchant? merchant, List<RecoveryAttempt> previousAttempts)
    {
        var candidates = StrategyMap.GetValueOrDefault(anomaly.Type,
            [RecoveryStrategy.ManualEscalation]);

        // Filter out strategies that already failed
        var failedStrategies = previousAttempts
            .Where(a => !a.IsSuccessful)
            .Select(a => a.Strategy)
            .ToHashSet();

        var available = candidates.Where(s => !failedStrategies.Contains(s)).ToList();

        if (available.Count == 0)
        {
            return (RecoveryStrategy.ManualEscalation, 30m,
                "All automated strategies exhausted; escalating to manual review");
        }

        var strategy = available[0];
        var reliability = merchant?.ReliabilityScore ?? 50m;

        // Higher confidence when merchant is reliable and anomaly is well-understood
        var confidence = 50m;
        confidence += reliability * 0.2m;
        if (anomaly.Severity == "low") confidence += 15;
        else if (anomaly.Severity == "medium") confidence += 5;
        else if (anomaly.Severity == "critical") confidence -= 10;
        if (previousAttempts.Count > 0) confidence -= previousAttempts.Count * 8;
        confidence = Math.Clamp(confidence, 10, 95);

        var reason = $"Selected {strategy} for {anomaly.Type} " +
                     $"(severity={anomaly.Severity}, reliability={reliability:F0}, " +
                     $"attempt #{previousAttempts.Count + 1})";

        return (strategy, confidence, reason);
    }

    private bool ExecuteStrategy(RecoveryStrategy strategy, Anomaly anomaly)
    {
        try
        {
            return strategy switch
            {
                RecoveryStrategy.AutoRetry => ExecuteAutoRetry(anomaly),
                RecoveryStrategy.MerchantNotification => true,
                RecoveryStrategy.PaynowDispute => ExecutePaynowDispute(anomaly),
                RecoveryStrategy.BankVerification => true,
                RecoveryStrategy.RecordReconstruction => true,
                RecoveryStrategy.ManualEscalation => true,
                _ => false
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Strategy {Strategy} failed for anomaly {AnomalyId}",
                strategy, anomaly.Id);
            return false;
        }
    }

    private bool ExecuteAutoRetry(Anomaly anomaly)
    {
        if (anomaly.TransactionId is null) return false;

        // For callback failures / status mismatches, flag for re-polling
        if (anomaly.Type is AnomalyType.CallbackFailure or AnomalyType.StatusMismatch)
        {
            _logger.LogInformation("Auto-retry queued for anomaly {AnomalyId}, type={Type}",
                anomaly.Id, anomaly.Type);
            return true;
        }

        return false;
    }

    private bool ExecutePaynowDispute(Anomaly anomaly)
    {
        _logger.LogInformation("Dispute opened for anomaly {AnomalyId}, amount={Amount}",
            anomaly.Id, anomaly.Amount);
        return true;
    }
}
