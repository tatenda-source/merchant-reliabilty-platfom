using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;
using MRP.Infrastructure.Persistence;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class IntelligenceController : ControllerBase
{
    private readonly MrpDbContext _db;
    private readonly IAgentTaskRepository _taskRepo;

    public IntelligenceController(MrpDbContext db, IAgentTaskRepository taskRepo)
    {
        _db = db;
        _taskRepo = taskRepo;
    }

    // --- Settlement Intelligence ---

    [HttpGet("settlement/predictions")]
    public async Task<ActionResult<List<SettlementPredictionDto>>> GetSettlementPredictions(
        [FromQuery] Guid? merchantId, CancellationToken ct)
    {
        var query = _db.SettlementPredictions.AsQueryable();

        if (merchantId.HasValue)
            query = query.Where(s => s.MerchantId == merchantId.Value);

        var predictions = await query
            .OrderByDescending(s => s.CreatedAt)
            .Take(100)
            .Select(s => new SettlementPredictionDto(
                s.Id, s.TransactionId, s.MerchantId,
                s.RiskScore, s.PredictedSettlementTime, s.Confidence,
                s.PaymentMethod.ToString(), s.RiskFactors,
                s.CreatedAt, s.ActualSettlementTime))
            .ToListAsync(ct);

        return Ok(predictions);
    }

    [HttpGet("settlement/high-risk")]
    public async Task<ActionResult<List<SettlementPredictionDto>>> GetHighRiskSettlements(
        [FromQuery] decimal threshold = 70, CancellationToken ct = default)
    {
        var predictions = await _db.SettlementPredictions
            .Where(s => s.RiskScore >= threshold && s.ActualSettlementTime == null)
            .OrderByDescending(s => s.RiskScore)
            .Take(50)
            .Select(s => new SettlementPredictionDto(
                s.Id, s.TransactionId, s.MerchantId,
                s.RiskScore, s.PredictedSettlementTime, s.Confidence,
                s.PaymentMethod.ToString(), s.RiskFactors,
                s.CreatedAt, s.ActualSettlementTime))
            .ToListAsync(ct);

        return Ok(predictions);
    }

    [HttpGet("settlement/summary")]
    public async Task<ActionResult<SettlementRiskSummaryDto>> GetSettlementSummary(CancellationToken ct)
    {
        var total = await _db.SettlementPredictions.CountAsync(ct);
        var highRisk = await _db.SettlementPredictions.CountAsync(s => s.RiskScore >= 70, ct);
        var avgRisk = total > 0
            ? await _db.SettlementPredictions.AverageAsync(s => (double)s.RiskScore, ct)
            : 0;

        var withActual = await _db.SettlementPredictions
            .CountAsync(s => s.ActualSettlementTime != null, ct);
        var accurate = await _db.SettlementPredictions
            .CountAsync(s => s.ActualSettlementTime != null && s.WasAccurate, ct);
        var accuracy = withActual > 0 ? (decimal)accurate / withActual * 100 : 0;

        return Ok(new SettlementRiskSummaryDto(total, highRisk, Math.Round((decimal)avgRisk, 1), Math.Round(accuracy, 1)));
    }

    [HttpPost("settlement/analyse")]
    public async Task<ActionResult> TriggerSettlementAnalysis(
        [FromBody] TriggerSettlementAnalysisRequest request, CancellationToken ct)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.SettlementIntelligence,
            TaskType = "SettlementAnalysis",
            InputPayload = JsonSerializer.Serialize(new { request.MerchantId }),
            Status = "queued",
            Priority = 7,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddAsync(task, ct);
        return Accepted(new { taskId = task.Id });
    }

    // --- Merchant Behaviour ---

    [HttpGet("behaviour/{merchantId:guid}")]
    public async Task<ActionResult<MerchantBehaviourDto>> GetMerchantBehaviour(
        Guid merchantId, CancellationToken ct)
    {
        var profile = await _db.MerchantBehaviourProfiles
            .FirstOrDefaultAsync(m => m.MerchantId == merchantId, ct);

        if (profile is null)
            return NotFound();

        List<string>? alerts = null;
        if (!string.IsNullOrEmpty(profile.ActiveAlerts))
            alerts = JsonSerializer.Deserialize<List<string>>(profile.ActiveAlerts);

        return Ok(new MerchantBehaviourDto(
            profile.MerchantId,
            profile.AvgTransactionsPerMinute,
            profile.AvgTransactionsPerHour,
            profile.RetryRate,
            profile.DuplicateRate,
            profile.CallbackFailureRate,
            profile.RiskScore,
            profile.PeakTransactionsPerHour,
            alerts,
            profile.LastAnalysedAt));
    }

    [HttpGet("behaviour/high-risk")]
    public async Task<ActionResult<List<MerchantBehaviourDto>>> GetHighRiskMerchants(
        [FromQuery] decimal threshold = 50, CancellationToken ct = default)
    {
        var profiles = await _db.MerchantBehaviourProfiles
            .Where(m => m.RiskScore >= threshold)
            .OrderByDescending(m => m.RiskScore)
            .Select(m => new MerchantBehaviourDto(
                m.MerchantId,
                m.AvgTransactionsPerMinute,
                m.AvgTransactionsPerHour,
                m.RetryRate,
                m.DuplicateRate,
                m.CallbackFailureRate,
                m.RiskScore,
                m.PeakTransactionsPerHour,
                null,
                m.LastAnalysedAt))
            .ToListAsync(ct);

        return Ok(profiles);
    }

    [HttpPost("behaviour/analyse")]
    public async Task<ActionResult> TriggerBehaviourAnalysis(
        [FromBody] TriggerBehaviourAnalysisRequest request, CancellationToken ct)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.MerchantBehaviour,
            TaskType = "BehaviourAnalysis",
            InputPayload = JsonSerializer.Serialize(new { request.MerchantId }),
            Status = "queued",
            Priority = 8,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddAsync(task, ct);
        return Accepted(new { taskId = task.Id });
    }

    // --- Recovery Intelligence ---

    [HttpGet("recovery/decisions")]
    public async Task<ActionResult<List<RecoveryStrategyDecisionDto>>> GetRecoveryDecisions(
        [FromQuery] Guid? anomalyId, CancellationToken ct)
    {
        var query = _db.RecoveryStrategyDecisions.AsQueryable();

        if (anomalyId.HasValue)
            query = query.Where(r => r.AnomalyId == anomalyId.Value);

        var decisions = await query
            .OrderByDescending(r => r.DecidedAt)
            .Take(100)
            .Select(r => new RecoveryStrategyDecisionDto(
                r.Id, r.AnomalyId, r.ChosenStrategy.ToString(),
                r.ConfidenceScore, r.DecisionReason,
                r.MerchantReliabilityAtDecision, r.FinancialRiskAmount,
                r.WasEffective, r.DecidedAt))
            .ToListAsync(ct);

        return Ok(decisions);
    }

    [HttpGet("recovery/stats")]
    public async Task<ActionResult<RecoveryIntelligenceStatsDto>> GetRecoveryIntelligenceStats(
        CancellationToken ct)
    {
        var totalDecisions = await _db.RecoveryStrategyDecisions.CountAsync(ct);
        var effective = await _db.RecoveryStrategyDecisions
            .CountAsync(r => r.WasEffective, ct);
        var autonomousRate = totalDecisions > 0
            ? (decimal)effective / totalDecisions * 100 : 0;
        var avgConfidence = totalDecisions > 0
            ? (decimal)await _db.RecoveryStrategyDecisions
                .AverageAsync(r => (double)r.ConfidenceScore, ct)
            : 0;

        var strategyRates = new Dictionary<string, decimal>();
        foreach (var strategy in Enum.GetValues<RecoveryStrategy>())
        {
            var total = await _db.RecoveryStrategyDecisions
                .CountAsync(r => r.ChosenStrategy == strategy, ct);
            if (total > 0)
            {
                var success = await _db.RecoveryStrategyDecisions
                    .CountAsync(r => r.ChosenStrategy == strategy && r.WasEffective, ct);
                strategyRates[strategy.ToString()] = Math.Round((decimal)success / total * 100, 1);
            }
        }

        return Ok(new RecoveryIntelligenceStatsDto(
            totalDecisions,
            Math.Round(autonomousRate, 1),
            Math.Round(avgConfidence, 1),
            strategyRates));
    }

    [HttpPost("recovery/decide")]
    public async Task<ActionResult> TriggerIntelligentRecovery(
        [FromBody] TriggerIntelligentRecoveryRequest request, CancellationToken ct)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.RecoveryIntelligence,
            TaskType = "RecoveryDecision",
            InputPayload = JsonSerializer.Serialize(new
            {
                request.AnomalyId,
                request.MerchantId,
                request.TransactionAmount
            }),
            Status = "queued",
            Priority = 9,
            CreatedAt = DateTime.UtcNow
        };

        await _taskRepo.AddAsync(task, ct);
        return Accepted(new { taskId = task.Id });
    }
}
