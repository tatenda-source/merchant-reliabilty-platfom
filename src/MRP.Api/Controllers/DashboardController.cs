using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MRP.Agents.Recovery;
using MRP.Application.DTOs;
using MRP.Domain.Enums;
using MRP.Infrastructure.Persistence;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DashboardController : ControllerBase
{
    private readonly MrpDbContext _db;
    private readonly RecoveryChannel _recoveryChannel;

    public DashboardController(MrpDbContext db, RecoveryChannel recoveryChannel)
    {
        _db = db;
        _recoveryChannel = recoveryChannel;
    }

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

    [HttpGet("pipelines/status")]
    public async Task<ActionResult> GetPipelineStatus(CancellationToken ct)
    {
        var unresolvedAnomalies = await _db.Anomalies.CountAsync(a => !a.IsResolved, ct);
        var totalRecoveries = await _db.RecoveryAttempts.CountAsync(ct);
        var successfulRecoveries = await _db.RecoveryAttempts.CountAsync(r => r.IsSuccessful, ct);
        var highRiskSettlements = await _db.Settlements.CountAsync(s => s.RiskScore >= 70 && s.ActualSettlementTime == null, ct);
        var highRiskMerchants = await _db.MerchantProfiles.CountAsync(m => m.BehaviourRiskScore >= 50, ct);

        var channelMetrics = _recoveryChannel.GetMetrics();

        // Strategy success rates (last 90 days)
        var cutoff = DateTime.UtcNow.AddDays(-90);
        var strategyStats = await _db.RecoveryAttempts
            .Where(r => r.AttemptedAt >= cutoff)
            .GroupBy(r => r.Strategy)
            .Select(g => new
            {
                Strategy = g.Key.ToString(),
                Total = g.Count(),
                Successful = g.Count(r => r.IsSuccessful),
                AvgConfidence = g.Average(r => r.ConfidenceScore)
            })
            .ToListAsync(ct);

        return Ok(new
        {
            ingestion = new { status = "Active" },
            intelligence = new
            {
                status = "Active",
                unresolvedAnomalies,
                highRiskSettlements,
                highRiskMerchants
            },
            recovery = new
            {
                status = "Active",
                totalAttempts = totalRecoveries,
                successRate = totalRecoveries > 0
                    ? Math.Round((decimal)successfulRecoveries / totalRecoveries * 100, 1) : 0m,
                channel = new
                {
                    channelMetrics.Enqueued,
                    channelMetrics.Dropped,
                    channelMetrics.Processed,
                    channelMetrics.Failed,
                    dropRate = channelMetrics.Enqueued > 0
                        ? Math.Round((decimal)channelMetrics.Dropped / channelMetrics.Enqueued * 100, 1) : 0m
                },
                strategyTrends = strategyStats.Select(s => new
                {
                    s.Strategy,
                    s.Total,
                    s.Successful,
                    SuccessRate = s.Total > 0 ? Math.Round((decimal)s.Successful / s.Total * 100, 1) : 0m,
                    AvgConfidence = Math.Round((decimal)s.AvgConfidence, 1)
                })
            }
        });
    }
}
