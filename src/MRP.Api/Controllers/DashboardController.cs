using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MRP.Application.DTOs;
using MRP.Domain.Enums;
using MRP.Infrastructure.Persistence;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly MrpDbContext _db;

    public DashboardController(MrpDbContext db) => _db = db;

    [HttpGet("metrics")]
    public async Task<ActionResult<DashboardMetricsDto>> GetMetrics(CancellationToken ct)
    {
        var totalTx = await _db.Transactions.CountAsync(ct);
        var matchedTx = await _db.Transactions
            .CountAsync(t => t.Status == TransactionStatus.Paid, ct);
        var failedTx = await _db.Transactions
            .CountAsync(t => t.Status == TransactionStatus.Failed, ct);
        var activeMerchants = await _db.Merchants
            .CountAsync(m => m.IsActive, ct);
        var anomalies = await _db.Anomalies
            .CountAsync(a => !a.IsResolved, ct);
        var totalVolume = await _db.Transactions
            .SumAsync(t => t.Amount, ct);

        var totalRecoveryAttempts = await _db.RecoveryAttempts.CountAsync(ct);
        var successfulRecoveries = await _db.RecoveryAttempts
            .CountAsync(r => r.IsSuccessful, ct);
        var recoveryRate = totalRecoveryAttempts > 0
            ? (decimal)successfulRecoveries / totalRecoveryAttempts * 100 : 0;

        var reliability = totalTx > 0
            ? (decimal)matchedTx / totalTx * 100 : 100m;

        return Ok(new DashboardMetricsDto(
            ReliabilityScore: Math.Round(reliability, 1),
            TotalTransactions: totalTx,
            MatchedTransactions: matchedTx,
            FailedTransactions: failedTx,
            ActiveMerchants: activeMerchants,
            AnomaliesDetected: anomalies,
            RecoveryRate: Math.Round(recoveryRate, 1),
            TotalVolume: totalVolume,
            RecoveredAmount: 0));
    }

    [HttpGet("agents/status")]
    public async Task<ActionResult<List<AgentStatusDto>>> GetAgentStatus(CancellationToken ct)
    {
        var agentTypes = new[] { AgentType.Onboarding, AgentType.TransactionIntelligence, AgentType.Recovery };
        var statuses = new List<AgentStatusDto>();

        foreach (var type in agentTypes)
        {
            var pending = await _db.AgentTasks
                .CountAsync(t => t.AgentType == type && t.Status == "queued", ct);
            var completed = await _db.AgentTasks
                .CountAsync(t => t.AgentType == type && t.Status == "completed", ct);
            var failed = await _db.AgentTasks
                .CountAsync(t => t.AgentType == type && t.Status == "failed", ct);
            var total = completed + failed;
            var successRate = total > 0 ? (decimal)completed / total * 100 : 100m;

            var lastRun = await _db.AgentTasks
                .Where(t => t.AgentType == type && t.CompletedAt.HasValue)
                .OrderByDescending(t => t.CompletedAt)
                .Select(t => t.CompletedAt)
                .FirstOrDefaultAsync(ct);

            statuses.Add(new AgentStatusDto(
                Name: type switch
                {
                    AgentType.Onboarding => "Onboarding Agent",
                    AgentType.TransactionIntelligence => "Transaction Intelligence Agent",
                    AgentType.Recovery => "Recovery Agent",
                    _ => type.ToString()
                },
                Type: type.ToString(),
                State: "Running",
                LastRunAt: lastRun,
                PendingTasks: pending,
                CompletedTasks: completed,
                SuccessRate: Math.Round(successRate, 1)));
        }

        return Ok(statuses);
    }
}
