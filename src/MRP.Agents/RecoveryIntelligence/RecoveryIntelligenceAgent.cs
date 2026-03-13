using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.RecoveryIntelligence;

public class RecoveryIntelligenceAgent : AgentBase
{
    public override string Name => "RecoveryIntelligenceAgent";
    public override AgentType Type => AgentType.RecoveryIntelligence;

    public RecoveryIntelligenceAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<RecoveryIntelligenceAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(2))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var recoveryRepo = scope.ServiceProvider.GetRequiredService<IRecoveryRepository>();
        var recoveryIntelRepo = scope.ServiceProvider.GetRequiredService<IRecoveryIntelligenceRepository>();
        var merchantRepo = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var behaviourRepo = scope.ServiceProvider.GetRequiredService<IMerchantBehaviourRepository>();
        var agentTaskRepo = scope.ServiceProvider.GetRequiredService<IAgentTaskRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<RecoveryIntelligenceInput>(task.InputPayload ?? "{}");
        var anomalyId = input?.AnomalyId ?? Guid.Empty;
        var merchantId = input?.MerchantId ?? Guid.Empty;
        var anomalyType = input?.AnomalyType ?? AnomalyType.CallbackFailure;
        var transactionAmount = input?.TransactionAmount ?? 0;

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

        // Gather intelligence for strategy decision
        var behaviourProfile = await behaviourRepo.GetByMerchantAsync(merchantId, ct);
        var previousAttempts = await recoveryRepo.GetAttemptsByAnomalyAsync(anomalyId, ct);

        // Get historical strategy success rates
        var strategyRates = new Dictionary<RecoveryStrategy, decimal>();
        foreach (var strategy in Enum.GetValues<RecoveryStrategy>())
        {
            strategyRates[strategy] = await recoveryIntelRepo.GetStrategySuccessRateAsync(strategy, ct);
        }

        // Make intelligent strategy decision
        var (chosenStrategy, confidence, reason) = DecideStrategy(
            anomalyType,
            merchant.ReliabilityScore,
            behaviourProfile?.RiskScore ?? 0,
            transactionAmount,
            previousAttempts,
            strategyRates);

        var decision = new RecoveryStrategyDecision
        {
            Id = Guid.NewGuid(),
            AnomalyId = anomalyId,
            MerchantId = merchantId,
            ChosenStrategy = chosenStrategy,
            ConfidenceScore = confidence,
            DecisionReason = reason,
            MerchantReliabilityAtDecision = merchant.ReliabilityScore,
            FinancialRiskAmount = transactionAmount,
            DecidedAt = DateTime.UtcNow
        };

        await recoveryIntelRepo.AddAsync(decision, ct);

        await eventBus.PublishAsync(
            new RecoveryStrategyDecided(decision.Id, anomalyId, chosenStrategy, confidence), ct);

        // Queue the recovery task with the chosen strategy
        var recoveryTask = new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.Recovery,
            TaskType = "IntelligentRecovery",
            InputPayload = JsonSerializer.Serialize(new
            {
                AnomalyId = anomalyId,
                AnomalyType = anomalyType,
                TransactionId = input?.TransactionId,
                RecommendedStrategy = chosenStrategy.ToString(),
                DecisionId = decision.Id
            }),
            Status = "queued",
            Priority = CalculatePriority(transactionAmount, anomalyType),
            CreatedAt = DateTime.UtcNow
        };
        await agentTaskRepo.AddAsync(recoveryTask, ct);

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = true,
            OutputPayload = JsonSerializer.Serialize(new
            {
                decisionId = decision.Id,
                anomalyId,
                chosenStrategy = chosenStrategy.ToString(),
                confidence = Math.Round(confidence, 1),
                reason,
                merchantReliability = merchant.ReliabilityScore,
                financialRisk = transactionAmount,
                queuedRecoveryTaskId = recoveryTask.Id
            }),
            CompletedAt = DateTime.UtcNow
        };
    }

    private static (RecoveryStrategy strategy, decimal confidence, string reason) DecideStrategy(
        AnomalyType anomalyType,
        decimal merchantReliability,
        decimal behaviourRiskScore,
        decimal transactionAmount,
        List<RecoveryAttempt> previousAttempts,
        Dictionary<RecoveryStrategy, decimal> strategyRates)
    {
        var attemptCount = previousAttempts.Count;
        var lastAttemptFailed = previousAttempts.LastOrDefault()?.IsSuccessful == false;

        // High-reliability merchant with record issues — auto-reconstruct
        if (anomalyType == AnomalyType.MissingMerchantRecord
            && merchantReliability >= 80
            && behaviourRiskScore < 30)
        {
            return (RecoveryStrategy.RecordReconstruction, 88,
                $"High-reliability merchant ({merchantReliability}%) with low risk — auto-reconstructing merchant record");
        }

        // Missing bank record — verify with bank first if merchant is reliable
        if (anomalyType == AnomalyType.MissingBankRecord && merchantReliability >= 60)
        {
            return (RecoveryStrategy.BankVerification, 82,
                $"Missing bank record for reliable merchant ({merchantReliability}%) — requesting bank verification before dispute");
        }

        // Callback failures — auto-retry if merchant behaviour is stable
        if (anomalyType == AnomalyType.CallbackFailure && behaviourRiskScore < 40 && attemptCount < 2)
        {
            return (RecoveryStrategy.AutoRetry, 75,
                $"Callback failure with stable merchant behaviour (risk: {behaviourRiskScore}) — retrying");
        }

        // Status mismatch — escalate for high-value transactions
        if (anomalyType == AnomalyType.StatusMismatch && transactionAmount > 500)
        {
            return (RecoveryStrategy.ManualEscalation, 90,
                $"Status mismatch on high-value transaction (${transactionAmount}) — escalating for manual review");
        }

        // Amount discrepancy — always manual escalation
        if (anomalyType == AnomalyType.AmountDiscrepancy)
        {
            var confidence = transactionAmount > 100 ? 95m : 80m;
            return (RecoveryStrategy.ManualEscalation, confidence,
                $"Amount discrepancy detected (${transactionAmount}) — requires human verification");
        }

        // Low-reliability merchant — notify rather than auto-fix
        if (merchantReliability < 50)
        {
            return (RecoveryStrategy.MerchantNotification, 70,
                $"Low merchant reliability ({merchantReliability}%) — notifying merchant to investigate");
        }

        // Previous attempts failed — escalate strategy
        if (attemptCount >= 2 && lastAttemptFailed)
        {
            // Check which strategy has best historical success rate
            var bestStrategy = strategyRates
                .Where(s => s.Key != RecoveryStrategy.AutoRetry) // Don't retry what already failed
                .OrderByDescending(s => s.Value)
                .Select(s => s.Key)
                .FirstOrDefault();

            if (bestStrategy == default)
                bestStrategy = RecoveryStrategy.PaynowDispute;

            return (bestStrategy, 65,
                $"Previous {attemptCount} attempts failed — escalating to {bestStrategy} (historical success: {strategyRates.GetValueOrDefault(bestStrategy, 0):F0}%)");
        }

        // Default: use the historically most successful strategy for this anomaly type
        var defaultStrategy = anomalyType switch
        {
            AnomalyType.MissingPaynowRecord => RecoveryStrategy.PaynowDispute,
            AnomalyType.DuplicateTransaction => RecoveryStrategy.ManualEscalation,
            AnomalyType.VelocityAnomaly => RecoveryStrategy.MerchantNotification,
            AnomalyType.SettlementDelay => RecoveryStrategy.PaynowDispute,
            _ => RecoveryStrategy.AutoRetry
        };

        return (defaultStrategy, 60,
            $"Standard strategy for {anomalyType} with merchant reliability {merchantReliability}%");
    }

    private static int CalculatePriority(decimal amount, AnomalyType type)
    {
        // Higher priority = processed first (higher number)
        var priority = 5;

        if (amount > 1000) priority += 3;
        else if (amount > 500) priority += 2;
        else if (amount > 100) priority += 1;

        priority += type switch
        {
            AnomalyType.AmountDiscrepancy => 2,
            AnomalyType.MissingBankRecord => 2,
            AnomalyType.MissingPaynowRecord => 1,
            _ => 0
        };

        return Math.Min(priority, 10);
    }
}

public record RecoveryIntelligenceInput(
    Guid AnomalyId,
    Guid MerchantId,
    AnomalyType AnomalyType,
    Guid? TransactionId,
    decimal TransactionAmount);
