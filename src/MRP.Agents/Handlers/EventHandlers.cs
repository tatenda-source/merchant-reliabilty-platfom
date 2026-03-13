using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Recovery;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;
using MRP.Infrastructure.EventBus;

namespace MRP.Agents.Handlers;

/// <summary>
/// When a transaction is received, trigger settlement risk prediction
/// and merchant behaviour analysis in a background scope.
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

    public Task Handle(EventNotification<TransactionReceived> notification, CancellationToken ct)
    {
        var e = notification.Event;
        _logger.LogDebug("Handling TransactionReceived: {TransactionId} for merchant {MerchantId}",
            e.TransactionId, e.MerchantId);

        // Fire-and-forget in a new scope to avoid blocking the caller
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

        return Task.CompletedTask;
    }
}

/// <summary>
/// When an anomaly is detected, enqueue it into the bounded recovery channel
/// instead of calling RecoveryEngine directly on the MediatR thread.
/// </summary>
public class AnomalyDetectedHandler : INotificationHandler<EventNotification<AnomalyDetected>>
{
    private readonly RecoveryChannel _channel;
    private readonly ILogger<AnomalyDetectedHandler> _logger;

    public AnomalyDetectedHandler(RecoveryChannel channel, ILogger<AnomalyDetectedHandler> logger)
    {
        _channel = channel;
        _logger = logger;
    }

    public async Task Handle(EventNotification<AnomalyDetected> notification, CancellationToken ct)
    {
        var e = notification.Event;

        // Only auto-recover high/critical anomalies
        if (e.Severity is not ("high" or "critical")) return;

        var item = new RecoveryWorkItem(e.AnomalyId, e.Severity, DateTime.UtcNow);

        if (_channel.Writer.TryWrite(item))
        {
            _logger.LogDebug("Enqueued recovery for anomaly {AnomalyId} (severity={Severity})",
                e.AnomalyId, e.Severity);
        }
        else
        {
            _logger.LogWarning("Recovery channel full — dropped anomaly {AnomalyId}. Consider scaling.", e.AnomalyId);
        }
    }
}
