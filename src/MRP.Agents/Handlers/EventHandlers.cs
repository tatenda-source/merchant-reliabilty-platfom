using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;
using MRP.Infrastructure.EventBus;

namespace MRP.Agents.Handlers;

/// <summary>
/// When a transaction is received, trigger settlement risk prediction
/// and merchant behaviour analysis.
/// </summary>
public class TransactionReceivedHandler : INotificationHandler<EventNotification<TransactionReceived>>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TransactionReceivedHandler> _logger;

    public TransactionReceivedHandler(IServiceScopeFactory scopeFactory, ILogger<TransactionReceivedHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(EventNotification<TransactionReceived> notification, CancellationToken ct)
    {
        var e = notification.Event;
        _logger.LogDebug("Handling TransactionReceived: {TransactionId} for merchant {MerchantId}", e.TransactionId, e.MerchantId);

        // Fire-and-forget in a new scope to avoid circular publish loops
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var intelligence = scope.ServiceProvider.GetRequiredService<IIntelligenceEngine>();

            try
            {
                await intelligence.PredictSettlementRiskAsync(e.TransactionId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Settlement prediction failed for tx {TransactionId}", e.TransactionId);
            }

            try
            {
                await intelligence.AnalyseMerchantBehaviourAsync(e.MerchantId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Behaviour analysis failed for merchant {MerchantId}", e.MerchantId);
            }
        }, ct);
    }
}

/// <summary>
/// When an anomaly is detected, trigger automatic recovery.
/// </summary>
public class AnomalyDetectedHandler : INotificationHandler<EventNotification<AnomalyDetected>>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AnomalyDetectedHandler> _logger;

    public AnomalyDetectedHandler(IServiceScopeFactory scopeFactory, ILogger<AnomalyDetectedHandler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task Handle(EventNotification<AnomalyDetected> notification, CancellationToken ct)
    {
        var e = notification.Event;

        // Only auto-recover high/critical anomalies
        if (e.Severity is not ("high" or "critical")) return;

        _logger.LogDebug("Auto-recovering anomaly {AnomalyId} (severity={Severity})", e.AnomalyId, e.Severity);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var recovery = scope.ServiceProvider.GetRequiredService<IRecoveryEngine>();

            try
            {
                await recovery.RecoverAsync(e.AnomalyId, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Auto-recovery failed for anomaly {AnomalyId}", e.AnomalyId);
            }
        }, ct);
    }
}
