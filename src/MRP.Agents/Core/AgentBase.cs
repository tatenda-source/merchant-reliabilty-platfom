using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Core;

public abstract class AgentBase : BackgroundService, IAgent
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger _logger;
    private readonly TimeSpan _pollInterval;

    public abstract string Name { get; }
    public abstract AgentType Type { get; }

    protected AgentBase(
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        TimeSpan pollInterval)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _pollInterval = pollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Agent {Name} starting with poll interval {Interval}s",
            Name, _pollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var taskRepo = scope.ServiceProvider.GetRequiredService<IAgentTaskRepository>();

                var task = await taskRepo.DequeueAsync(Type, stoppingToken);

                if (task is not null)
                {
                    _logger.LogInformation("Agent {Name} executing task {TaskId} ({TaskType})",
                        Name, task.Id, task.TaskType);

                    task.Status = "running";
                    task.StartedAt = DateTime.UtcNow;
                    await taskRepo.UpdateAsync(task, stoppingToken);

                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var result = await ExecuteAsync(task, stoppingToken);
                    sw.Stop();
                    result.Duration = sw.Elapsed;

                    task.Status = result.IsSuccess ? "completed" : "failed";
                    task.CompletedAt = DateTime.UtcNow;
                    await taskRepo.UpdateAsync(task, stoppingToken);
                    await taskRepo.SaveResultAsync(result, stoppingToken);

                    _logger.LogInformation(
                        "Agent {Name} task {TaskId} {Status} in {Duration}ms",
                        Name, task.Id, task.Status, sw.ElapsedMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Agent {Name} encountered an error", Name);
            }

            await Task.Delay(_pollInterval, stoppingToken);
        }

        _logger.LogInformation("Agent {Name} stopped", Name);
    }

    public abstract Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct);

    public virtual Task<bool> HealthCheckAsync(CancellationToken ct)
        => Task.FromResult(true);

    protected IServiceScope CreateScope() => _scopeFactory.CreateScope();
}
