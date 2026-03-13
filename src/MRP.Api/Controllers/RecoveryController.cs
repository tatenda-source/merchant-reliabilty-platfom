using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class RecoveryController : ControllerBase
{
    private readonly IRecoveryRepository _recoveryRepo;
    private readonly IAgentTaskRepository _taskRepo;

    public RecoveryController(
        IRecoveryRepository recoveryRepo,
        IAgentTaskRepository taskRepo)
    {
        _recoveryRepo = recoveryRepo;
        _taskRepo = taskRepo;
    }

    [HttpGet("queue")]
    public async Task<ActionResult<List<AnomalyDto>>> GetQueue(CancellationToken ct)
    {
        var anomalies = await _recoveryRepo.GetUnresolvedAnomaliesAsync(ct);
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
            a.IsSuccessful, a.ResultDetails, a.AttemptedAt, a.CompletedAt)).ToList());
    }

    [HttpPost("initiate")]
    public async Task<ActionResult> InitiateRecovery(
        [FromBody] InitiateRecoveryRequest request, CancellationToken ct)
    {
        await _taskRepo.AddAsync(new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.Recovery,
            TaskType = "recover",
            InputPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                anomalyId = request.AnomalyId
            }),
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        }, ct);

        return Accepted(new { message = "Recovery initiated" });
    }

    [HttpGet("stats")]
    public async Task<ActionResult> GetStats(CancellationToken ct)
    {
        var unresolved = await _recoveryRepo.GetUnresolvedAnomaliesAsync(ct);
        return Ok(new
        {
            queueSize = unresolved.Count,
            criticalCount = unresolved.Count(a => a.Severity == "critical"),
            highCount = unresolved.Count(a => a.Severity == "high"),
            mediumCount = unresolved.Count(a => a.Severity == "medium"),
            lowCount = unresolved.Count(a => a.Severity == "low")
        });
    }
}
