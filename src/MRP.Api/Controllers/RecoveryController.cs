using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecoveryController : ControllerBase
{
    private readonly IRecoveryRepository _recoveryRepo;
    private readonly IRecoveryEngine _recovery;

    public RecoveryController(IRecoveryRepository recoveryRepo, IRecoveryEngine recovery)
    {
        _recoveryRepo = recoveryRepo;
        _recovery = recovery;
    }

    [HttpGet("queue")]
    public async Task<ActionResult<List<AnomalyDto>>> GetQueue(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var anomalies = await _recoveryRepo.GetUnresolvedAnomaliesAsync(page, pageSize, ct);
        return Ok(anomalies.Select(a => new AnomalyDto(
            a.Id, a.Type.ToString(), a.Description, a.Severity,
            a.IsResolved, a.DetectedAt, a.ResolvedAt)).ToList());
    }

    [HttpGet("attempts/{anomalyId:guid}")]
    public async Task<ActionResult<List<RecoveryAttemptDto>>> GetAttempts(
        Guid anomalyId, CancellationToken ct)
    {
        var attempts = await _recoveryRepo.GetAttemptsByAnomalyAsync(anomalyId, ct);
        return Ok(attempts.Select(a => new RecoveryAttemptDto(
            a.Id, a.AnomalyId, a.Strategy.ToString(), a.AttemptNumber,
            a.IsSuccessful, a.ResultDetails, a.ConfidenceScore,
            a.DecisionReason, a.AttemptedAt, a.CompletedAt)).ToList());
    }

    [HttpPost("initiate")]
    public async Task<ActionResult<RecoveryAttemptDto>> InitiateRecovery(
        [FromBody] InitiateRecoveryRequest request, CancellationToken ct)
    {
        if (request.AnomalyId == Guid.Empty)
            return BadRequest(new { error = "AnomalyId is required" });

        var attempt = await _recovery.RecoverAsync(request.AnomalyId, ct);

        return Ok(new RecoveryAttemptDto(
            attempt.Id, attempt.AnomalyId, attempt.Strategy.ToString(),
            attempt.AttemptNumber, attempt.IsSuccessful, attempt.ResultDetails,
            attempt.ConfidenceScore, attempt.DecisionReason,
            attempt.AttemptedAt, attempt.CompletedAt));
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats(CancellationToken ct)
    {
        var stats = await _recoveryRepo.GetUnresolvedStatsAsync(ct);
        return Ok(stats);
    }
}
