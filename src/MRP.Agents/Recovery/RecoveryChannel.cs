using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Recovery;

/// <summary>
/// Configuration for the recovery channel and worker.
/// Bound from appsettings "Recovery" section.
/// </summary>
public sealed class RecoveryOptions
{
    public int ChannelCapacity { get; set; } = 500;
    public int MaxConcurrency { get; set; } = 3;
}

/// <summary>
/// Bounded channel that decouples anomaly detection from recovery execution.
/// MediatR handlers write to the channel; the background worker drains it.
/// Provides backpressure and graceful shutdown.
/// </summary>
public sealed class RecoveryChannel
{
    private readonly Channel<RecoveryWorkItem> _channel;

    // Observability counters
    private long _enqueued;
    private long _dropped;
    private long _processed;
    private long _failed;

    public RecoveryChannel(IOptions<RecoveryOptions> options)
    {
        var capacity = Math.Clamp(options.Value.ChannelCapacity, 10, 10_000);
        _channel = Channel.CreateBounded<RecoveryWorkItem>(new BoundedChannelOptions(capacity)
        {
            // DropWrite instead of DropOldest: TryWrite returns false when full,
            // so RecordDropped() actually fires and we get accurate drop metrics.
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ChannelWriter<RecoveryWorkItem> Writer => _channel.Writer;
    public ChannelReader<RecoveryWorkItem> Reader => _channel.Reader;

    public void RecordEnqueued() => Interlocked.Increment(ref _enqueued);
    public void RecordDropped() => Interlocked.Increment(ref _dropped);
    public void RecordProcessed() => Interlocked.Increment(ref _processed);
    public void RecordFailed() => Interlocked.Increment(ref _failed);

    public RecoveryChannelMetrics GetMetrics() => new(
        Enqueued: Interlocked.Read(ref _enqueued),
        Dropped: Interlocked.Read(ref _dropped),
        Processed: Interlocked.Read(ref _processed),
        Failed: Interlocked.Read(ref _failed));
}

public record RecoveryWorkItem(Guid AnomalyId, string Severity, DateTime EnqueuedAt);

public record RecoveryChannelMetrics(long Enqueued, long Dropped, long Processed, long Failed);

/// <summary>
/// Background worker that drains the recovery channel with concurrency control.
/// Each item gets its own DI scope so DB contexts aren't shared across threads.
/// </summary>
public sealed class RecoveryWorker : BackgroundService
{
    private readonly RecoveryChannel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<RecoveryWorker> _logger;
    private readonly int _maxConcurrency;

    public RecoveryWorker(
        RecoveryChannel channel, IServiceScopeFactory scopeFactory,
        ILogger<RecoveryWorker> logger, IOptions<RecoveryOptions> options)
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _maxConcurrency = Math.Clamp(options.Value.MaxConcurrency, 1, 20);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("RecoveryWorker started, max concurrency={MaxConcurrency}", _maxConcurrency);

        var semaphore = new SemaphoreSlim(_maxConcurrency);
        var inFlightTasks = new List<Task>();

        try
        {
            await foreach (var item in _channel.Reader.ReadAllAsync(stoppingToken))
            {
                await semaphore.WaitAsync(stoppingToken);

                // Don't pass stoppingToken to Task.Run — let in-flight work finish gracefully
                var task = Task.Run(async () =>
                {
                    try
                    {
                        var lag = DateTime.UtcNow - item.EnqueuedAt;
                        _logger.LogDebug("Recovery item lag: {LagMs}ms for anomaly {AnomalyId}",
                            lag.TotalMilliseconds, item.AnomalyId);

                        using var scope = _scopeFactory.CreateScope();
                        var recovery = scope.ServiceProvider.GetRequiredService<IRecoveryEngine>();
                        await recovery.RecoverAsync(item.AnomalyId, CancellationToken.None);

                        _channel.RecordProcessed();
                    }
                    catch (Exception ex)
                    {
                        _channel.RecordFailed();
                        _logger.LogWarning(ex, "Recovery failed for anomaly {AnomalyId}", item.AnomalyId);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                // Track in-flight tasks, pruning completed ones
                inFlightTasks.RemoveAll(t => t.IsCompleted);
                inFlightTasks.Add(task);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown: wait for in-flight recovery operations to complete
            _logger.LogInformation("RecoveryWorker shutting down, waiting for {Count} in-flight tasks", inFlightTasks.Count);
        }

        // Wait for all in-flight tasks with a timeout
        if (inFlightTasks.Count > 0)
        {
            var drainTask = Task.WhenAll(inFlightTasks);
            if (await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(30))) != drainTask)
            {
                _logger.LogWarning("RecoveryWorker drain timeout — {Count} tasks still running",
                    inFlightTasks.Count(t => !t.IsCompleted));
            }
        }

        semaphore.Dispose();
        _logger.LogInformation("RecoveryWorker stopped");
    }
}
