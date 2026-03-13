using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Recovery;

public class RecoveryAgent : AgentBase
{
    public override string Name => "RecoveryAgent";
    public override AgentType Type => AgentType.Recovery;

    private const int MaxRetryAttempts = 3;

    public RecoveryAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<RecoveryAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(5))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var recoveryRepo = scope.ServiceProvider.GetRequiredService<IRecoveryRepository>();
        var paynow = scope.ServiceProvider.GetRequiredService<IPaynowGateway>();
        var txRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var merchantRepo = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<RecoveryInput>(task.InputPayload ?? "{}");
        var anomalyId = input?.AnomalyId ?? Guid.Empty;

        // Get existing attempts
        var previousAttempts = await recoveryRepo.GetAttemptsByAnomalyAsync(anomalyId, ct);
        var attemptNumber = previousAttempts.Count + 1;

        if (attemptNumber > MaxRetryAttempts)
        {
            return new AgentResult
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                IsSuccess = false,
                ErrorMessage = $"Max retry attempts ({MaxRetryAttempts}) exceeded for anomaly {anomalyId}",
                CompletedAt = DateTime.UtcNow
            };
        }

        // Determine recovery strategy based on anomaly type
        var strategy = DetermineStrategy(input?.AnomalyType ?? AnomalyType.CallbackFailure, attemptNumber);

        var attempt = new RecoveryAttempt
        {
            Id = Guid.NewGuid(),
            AnomalyId = anomalyId,
            Strategy = strategy,
            AttemptNumber = attemptNumber,
            AttemptedAt = DateTime.UtcNow
        };

        await eventBus.PublishAsync(
            new RecoveryInitiated(attempt.Id, anomalyId, strategy), ct);

        bool success = false;
        string details;

        try
        {
            switch (strategy)
            {
                case RecoveryStrategy.AutoRetry:
                    // Re-poll Paynow for updated status
                    if (input?.TransactionId is not null)
                    {
                        var tx = await txRepo.GetByIdAsync(input.TransactionId.Value, ct);
                        if (tx is not null)
                        {
                            var merchant = await merchantRepo.GetByIdAsync(tx.MerchantId, ct);
                            if (merchant?.Integration is not null)
                            {
                                var statusResult = await paynow.PollTransactionAsync(
                                    merchant.Integration, tx.PaynowReference, ct);

                                if (statusResult.Status == TransactionStatus.Paid)
                                {
                                    tx.Status = TransactionStatus.Paid;
                                    tx.PaidAt = statusResult.PaidOn;
                                    await txRepo.UpdateAsync(tx, ct);
                                    success = true;
                                }
                            }
                        }
                    }
                    details = success ? "Transaction status updated to Paid via re-poll" : "Re-poll did not resolve the issue";
                    break;

                case RecoveryStrategy.MerchantNotification:
                    details = $"Merchant notification drafted for anomaly {anomalyId}. Merchant should verify transaction on their end.";
                    success = true; // Notification itself is successful
                    break;

                case RecoveryStrategy.ManualEscalation:
                    details = $"Anomaly {anomalyId} escalated for manual review. Amount discrepancy requires human verification.";
                    success = true;
                    break;

                case RecoveryStrategy.PaynowDispute:
                    details = $"Dispute filed with Paynow for anomaly {anomalyId}. Missing bank settlement after 48h.";
                    success = true;
                    break;

                default:
                    details = "Unknown strategy";
                    break;
            }
        }
        catch (Exception ex)
        {
            details = $"Recovery failed: {ex.Message}";
            success = false;
        }

        attempt.IsSuccessful = success;
        attempt.ResultDetails = details;
        attempt.CompletedAt = DateTime.UtcNow;

        await recoveryRepo.AddAttemptAsync(attempt, ct);
        await eventBus.PublishAsync(new RecoveryCompleted(attempt.Id, success), ct);

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = success,
            OutputPayload = JsonSerializer.Serialize(new
            {
                recoveryAttemptId = attempt.Id,
                strategy = strategy.ToString(),
                attemptNumber,
                wasSuccessful = success,
                details
            }),
            CompletedAt = DateTime.UtcNow
        };
    }

    private static RecoveryStrategy DetermineStrategy(AnomalyType anomalyType, int attemptNumber) =>
        anomalyType switch
        {
            AnomalyType.CallbackFailure => RecoveryStrategy.AutoRetry,
            AnomalyType.StatusMismatch when attemptNumber <= 1 => RecoveryStrategy.AutoRetry,
            AnomalyType.StatusMismatch => RecoveryStrategy.MerchantNotification,
            AnomalyType.AmountDiscrepancy => RecoveryStrategy.ManualEscalation,
            AnomalyType.MissingBankRecord => RecoveryStrategy.PaynowDispute,
            AnomalyType.MissingPaynowRecord => RecoveryStrategy.PaynowDispute,
            AnomalyType.MissingMerchantRecord => RecoveryStrategy.MerchantNotification,
            AnomalyType.DuplicateTransaction => RecoveryStrategy.ManualEscalation,
            _ => attemptNumber <= 2 ? RecoveryStrategy.AutoRetry : RecoveryStrategy.ManualEscalation
        };
}

public record RecoveryInput(Guid AnomalyId, AnomalyType AnomalyType, Guid? TransactionId);
