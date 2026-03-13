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

    [HttpGet("pipelines/status")]
    public async Task<ActionResult> GetPipelineStatus(CancellationToken ct)
    {
        var unresolvedAnomalies = await _db.Anomalies.CountAsync(a => !a.IsResolved, ct);
        var totalRecoveries = await _db.RecoveryAttempts.CountAsync(ct);
        var successfulRecoveries = await _db.RecoveryAttempts.CountAsync(r => r.IsSuccessful, ct);
        var highRiskSettlements = await _db.Settlements.CountAsync(s => s.RiskScore >= 70 && s.ActualSettlementTime == null, ct);
        var highRiskMerchants = await _db.MerchantProfiles.CountAsync(m => m.BehaviourRiskScore >= 50, ct);

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
                    ? Math.Round((decimal)successfulRecoveries / totalRecoveries * 100, 1) : 0m
            }
        });
    }
}
