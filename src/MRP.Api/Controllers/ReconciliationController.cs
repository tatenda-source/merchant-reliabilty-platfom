using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReconciliationController : ControllerBase
{
    private readonly IReconciliationRepository _reconRepo;
    private readonly IAgentTaskRepository _taskRepo;

    public ReconciliationController(
        IReconciliationRepository reconRepo,
        IAgentTaskRepository taskRepo)
    {
        _reconRepo = reconRepo;
        _taskRepo = taskRepo;
    }

    [HttpGet("reports")]
    public async Task<ActionResult<List<ReconciliationReportDto>>> GetReports(
        [FromQuery] Guid merchantId, CancellationToken ct)
    {
        var reports = await _reconRepo.GetByMerchantAsync(merchantId, ct);
        return Ok(reports.Select(MapToDto).ToList());
    }

    [HttpGet("reports/{id:guid}")]
    public async Task<ActionResult<ReconciliationReportDto>> GetReport(Guid id, CancellationToken ct)
    {
        var report = await _reconRepo.GetByIdAsync(id, ct);
        if (report is null) return NotFound();
        return Ok(MapToDto(report));
    }

    [HttpPost("trigger")]
    public async Task<ActionResult> TriggerReconciliation(
        [FromBody] TriggerReconciliationRequest request, CancellationToken ct)
    {
        await _taskRepo.AddAsync(new AgentTask
        {
            Id = Guid.NewGuid(),
            AgentType = AgentType.TransactionIntelligence,
            TaskType = "reconcile",
            InputPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                merchantId = request.MerchantId,
                periodStart = request.PeriodStart ?? DateTime.UtcNow.AddDays(-1),
                periodEnd = request.PeriodEnd ?? DateTime.UtcNow
            }),
            Priority = 0,
            CreatedAt = DateTime.UtcNow
        }, ct);

        return Accepted(new { message = "Reconciliation queued" });
    }

    private static ReconciliationReportDto MapToDto(ReconciliationReport r) => new(
        r.Id, r.MerchantId, r.PeriodStart, r.PeriodEnd, r.GeneratedAt,
        r.TotalTransactions, r.MatchedCount, r.UnmatchedCount, r.AnomalyCount,
        r.TotalVolume, r.MatchedVolume, r.DiscrepancyVolume,
        r.TotalTransactions > 0 ? (decimal)r.MatchedCount / r.TotalTransactions * 100 : 100m);
}
