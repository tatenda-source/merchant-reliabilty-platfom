using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Recovery;

/// <summary>
/// Bounded channel that decouples anomaly detection from recovery execution.
/// MediatR handlers write to the channel; the background worker drains it.
/// Provides backpressure (capacity 500) and graceful shutdown.
/// </summary>
public sealed class RecoveryChannel
{
    private readonly Channel<RecoveryWorkItem> _channel =
        Channel.CreateBounded<RecoveryWorkItem>(new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelWriter<RecoveryWorkItem> Writer => _channel.Writer;
    public ChannelReader<RecoveryWorkItem> Reader => _channel.Reader;
}

public record RecoveryWorkItem(Guid AnomalyId, string Severity, DateTime EnqueuedAt);

/// <summary>
/// Background worker that drains the recovery channel with concurrency control.
/// Each item gets its own DI scope so DB contexts aren't shared across threads.
/// </summary>
public sealed class RecoveryWorker : BackgroundService
{
    private readonly RecoveryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecoveryWorker> _logger;

    // Max concurrent recovery operations
    private const int MaxConcurrency = 3;

    public RecoveryWorker(
        RecoveryChannel channel, IServiceScopeFactory scopeFactory, ILogger<RecoveryWorker> logger)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecoveryWorker started, max concurrency={MaxConcurrency}", MaxConcurrency);

        using var semaphore = new SemaphoreSlim(MaxConcurrency);

        await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await semaphore.WaitAsync(stoppingToken);

            _ = Task.Run(async () =>
            {
                try
                {
                    var lag = DateTime.UtcNow - item.EnqueuedAt;
                    if (lag.TotalSeconds > 1)
                        _logger.LogDebug("Recovery item lag: {LagMs}ms for anomaly {AnomalyId}",
                            lag.TotalMilliseconds, item.AnomalyId);

                    using var scope = _scopeFactory.CreateScope();
                    var recovery = scope.ServiceProvider.GetRequiredService<IRecoveryEngine>();
                    await recovery.RecoverAsync(item.AnomalyId, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Recovery failed for anomaly {AnomalyId}", item.AnomalyId);
                }
                finally
                {
                    semaphore.Release();
                }
            }, stoppingToken);
        }
    }
}
