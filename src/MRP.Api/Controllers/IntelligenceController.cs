using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MRP.Application.DTOs;
using MRP.Domain.Interfaces;
using MRP.Infrastructure.Persistence;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IntelligenceController : ControllerBase
{
    private readonly MrpDbContext _db;
    private readonly IIntelligenceEngine _intelligence;

    public IntelligenceController(MrpDbContext db, IIntelligenceEngine intelligence)
    {
        _db = db;
        _intelligence = intelligence;
    }

    // --- Settlement Intelligence ---

    [HttpGet("settlement/predictions")]
    public async Task<ActionResult<List<SettlementPredictionDto>>> GetSettlementPredictions(
        [FromQuery] Guid? merchantId, CancellationToken ct)
    {
        var query = _db.Settlements.AsQueryable();
        if (merchantId.HasValue)
            query = query.Where(s => s.MerchantId == merchantId.Value);

        var settlements = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .Select(s => new SettlementPredictionDto(
                s.Id, s.TransactionId, s.MerchantId,
                s.RiskScore, s.PredictedSettlementTime, s.Confidence,
                s.PaymentMethod.ToString(), s.RiskFactors,
                s.CreatedAt, s.ActualSettlementTime))
            .ToListAsync(ct);

        return Ok(settlements);
    }

    [HttpGet("settlement/high-risk")]
    public async Task<ActionResult<List<SettlementPredictionDto>>> GetHighRiskSettlements(
        [FromQuery] decimal threshold = 70, CancellationToken ct = default)
    {
        var settlements = await _db.Settlements
            .Where(s => s.RiskScore >= threshold && s.ActualSettlementTime == null)
            .OrderByDescending(s => s.RiskScore)
            .Take(50)
            .Select(s => new SettlementPredictionDto(
                s.Id, s.TransactionId, s.MerchantId,
                s.RiskScore, s.PredictedSettlementTime, s.Confidence,
                s.PaymentMethod.ToString(), s.RiskFactors,
                s.CreatedAt, s.ActualSettlementTime))
            .ToListAsync(ct);

        return Ok(settlements);
    }

    [HttpGet("settlement/summary")]
    public async Task<ActionResult<SettlementRiskSummaryDto>> GetSettlementSummary(CancellationToken ct)
    {
        var total = await _db.Settlements.CountAsync(ct);
        var highRisk = await _db.Settlements.CountAsync(s => s.RiskScore >= 70, ct);
        var avgRisk = total > 0
            ? await _db.Settlements.AverageAsync(s => (double)s.RiskScore, ct) : 0;

        var withActual = await _db.Settlements
            .CountAsync(s => s.ActualSettlementTime != null, ct);
        var accurate = await _db.Settlements
            .CountAsync(s => s.ActualSettlementTime != null && s.WasAccurate, ct);
        var accuracy = withActual > 0 ? (decimal)accurate / withActual * 100 : 0;

        return Ok(new SettlementRiskSummaryDto(total, highRisk,
            Math.Round((decimal)avgRisk, 1), Math.Round(accuracy, 1)));
    }

    [HttpPost("settlement/analyse")]
    public async Task<ActionResult> TriggerSettlementAnalysis(
        [FromBody] TriggerSettlementAnalysisRequest request, CancellationToken ct)
    {
        await _intelligence.AnalyseMerchantBehaviourAsync(request.MerchantId, ct);
        return Accepted(new { message = "Settlement analysis triggered" });
    }

    // --- Merchant Behaviour ---

    [HttpGet("behaviour/{merchantId:guid}")]
    public async Task<ActionResult<MerchantBehaviourDto>> GetMerchantBehaviour(
        Guid merchantId, CancellationToken ct)
    {
        var profile = await _db.MerchantProfiles
            .FirstOrDefaultAsync(m => m.MerchantId == merchantId, ct);

        if (profile is null) return NotFound();

        List<string>? alerts = null;
        if (!string.IsNullOrEmpty(profile.ActiveAlerts))
            alerts = JsonSerializer.Deserialize<List<string>>(profile.ActiveAlerts);

        return Ok(new MerchantBehaviourDto(
            profile.MerchantId,
            profile.AvgTransactionsPerHour,
            profile.PeakTransactionsPerHour,
            profile.RetryRate,
            profile.DuplicateRate,
            profile.CallbackFailureRate,
            profile.BehaviourRiskScore,
            alerts,
            profile.LastAnalysedAt));
    }

    [HttpGet("behaviour/high-risk")]
    public async Task<ActionResult<List<MerchantBehaviourDto>>> GetHighRiskMerchants(
        [FromQuery] decimal threshold = 50, CancellationToken ct = default)
    {
        var profiles = await _db.MerchantProfiles
            .Where(m => m.BehaviourRiskScore >= threshold)
            .OrderByDescending(m => m.BehaviourRiskScore)
            .Select(m => new MerchantBehaviourDto(
                m.MerchantId,
                m.AvgTransactionsPerHour,
                m.PeakTransactionsPerHour,
                m.RetryRate,
                m.DuplicateRate,
                m.CallbackFailureRate,
                m.BehaviourRiskScore,
                null,
                m.LastAnalysedAt))
            .ToListAsync(ct);

        return Ok(profiles);
    }

    [HttpPost("behaviour/analyse")]
    public async Task<ActionResult> TriggerBehaviourAnalysis(
        [FromBody] TriggerBehaviourAnalysisRequest request, CancellationToken ct)
    {
        await _intelligence.AnalyseMerchantBehaviourAsync(request.MerchantId, ct);
        return Accepted(new { message = "Behaviour analysis triggered" });
    }

    // --- Recovery Intelligence ---

    [HttpGet("recovery/stats")]
    public async Task<ActionResult<RecoveryIntelligenceStatsDto>> GetRecoveryStats(CancellationToken ct)
    {
        var totalAttempts = await _db.RecoveryAttempts.CountAsync(ct);
        var successful = await _db.RecoveryAttempts.CountAsync(r => r.IsSuccessful, ct);
        var successRate = totalAttempts > 0
            ? (decimal)successful / totalAttempts * 100 : 0;
        var avgConfidence = totalAttempts > 0
            ? (decimal)await _db.RecoveryAttempts.AverageAsync(r => (double)r.ConfidenceScore, ct) : 0;

        return Ok(new RecoveryIntelligenceStatsDto(
            totalAttempts, Math.Round(successRate, 1), Math.Round(avgConfidence, 1)));
    }

    [HttpPost("recovery/decide")]
    public async Task<ActionResult> TriggerIntelligentRecovery(
        [FromBody] InitiateRecoveryRequest request, CancellationToken ct)
    {
        var recovery = HttpContext.RequestServices.GetRequiredService<IRecoveryEngine>();
        var attempt = await recovery.RecoverAsync(request.AnomalyId, ct);

        return Ok(new RecoveryAttemptDto(
            attempt.Id, attempt.AnomalyId, attempt.Strategy.ToString(),
            attempt.AttemptNumber, attempt.IsSuccessful, attempt.ResultDetails,
            attempt.ConfidenceScore, attempt.DecisionReason,
            attempt.AttemptedAt, attempt.CompletedAt));
    }
}
