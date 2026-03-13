using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReconciliationController : ControllerBase
{
    private readonly IReconciliationRepository _reconRepo;
    private readonly IIntelligenceEngine _intelligence;

    public ReconciliationController(
        IReconciliationRepository reconRepo, IIntelligenceEngine intelligence)
    {
        _reconRepo = reconRepo;
        _intelligence = intelligence;
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
        var periodStart = request.PeriodStart ?? DateTime.UtcNow.AddDays(-1);
        var periodEnd = request.PeriodEnd ?? DateTime.UtcNow;

        var report = await _intelligence.ReconcileAsync(
            request.MerchantId, periodStart, periodEnd, ct);

        return Ok(MapToDto(report));
    }

    private static ReconciliationReportDto MapToDto(ReconciliationReport r) => new(
        r.Id, r.MerchantId, r.PeriodStart, r.PeriodEnd, r.GeneratedAt,
        r.TotalTransactions, r.MatchedCount, r.UnmatchedCount, r.AnomalyCount,
        r.TotalVolume, r.MatchedVolume, r.DiscrepancyVolume,
        r.TotalTransactions > 0 ? (decimal)r.MatchedCount / r.TotalTransactions * 100 : 100m);
}
