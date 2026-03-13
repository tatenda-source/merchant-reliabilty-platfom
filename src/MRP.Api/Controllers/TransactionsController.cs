using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TransactionsController : ControllerBase
{
    private readonly ITransactionRepository _txRepo;
    private readonly IIngestionService _ingestion;

    public TransactionsController(ITransactionRepository txRepo, IIngestionService ingestion)
    {
        _txRepo = txRepo;
        _ingestion = ingestion;
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetByMerchant(
        [FromQuery] Guid merchantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
        if (merchantId == Guid.Empty)
            return BadRequest(new { error = "merchantId is required" });

        var transactions = await _txRepo.GetByMerchantAsync(
            merchantId,
            from ?? DateTime.UtcNow.AddDays(-30),
            to ?? DateTime.UtcNow,
            ct);

        return Ok(transactions.Select(MapToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<TransactionDto>> GetById(Guid id, CancellationToken ct)
    {
        var tx = await _txRepo.GetByIdAsync(id, ct);
        if (tx is null) return NotFound();
        return Ok(MapToDto(tx));
    }

    [HttpPost("ingest")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB max
    public async Task<ActionResult> IngestBatch(
        [FromBody] List<IngestTransactionRequest> requests, CancellationToken ct)
    {
        if (requests is null || requests.Count == 0)
            return BadRequest(new { error = "Request body must contain at least one transaction" });
        if (requests.Count > 1000)
            return BadRequest(new { error = "Maximum 1000 transactions per batch" });

        // Validate each request
        for (var i = 0; i < requests.Count; i++)
        {
            var r = requests[i];
            if (r.MerchantId == Guid.Empty)
                return BadRequest(new { error = $"Transaction [{i}]: MerchantId is required" });
            if (string.IsNullOrWhiteSpace(r.MerchantReference))
                return BadRequest(new { error = $"Transaction [{i}]: MerchantReference is required" });
            if (r.Amount <= 0)
                return BadRequest(new { error = $"Transaction [{i}]: Amount must be positive" });
            if (string.IsNullOrWhiteSpace(r.Currency))
                return BadRequest(new { error = $"Transaction [{i}]: Currency is required" });
        }

        // Route through IngestionService so events are published
        var transactions = requests.Select(r => new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = r.MerchantId,
            PaynowReference = r.PaynowReference ?? string.Empty,
            MerchantReference = r.MerchantReference,
            Amount = r.Amount,
            Currency = r.Currency,
            Status = Enum.TryParse<TransactionStatus>(r.Status, true, out var s)
                ? s : TransactionStatus.Pending,
            Source = SourceType.Merchant,
            PaymentMethod = Enum.TryParse<PaymentMethod>(r.PaymentMethod, true, out var pm)
                ? pm : PaymentMethod.EcoCash,
            CreatedAt = r.TransactionDate ?? DateTime.UtcNow
        });

        await _ingestion.IngestMerchantBatchAsync(transactions, ct);
        return Accepted(new { ingested = requests.Count });
    }

    private static TransactionDto MapToDto(Transaction t) => new(
        t.Id, t.MerchantId, t.PaynowReference, t.MerchantReference,
        t.Amount, t.Currency, t.Status.ToString(), t.Source.ToString(),
        t.PaymentMethod.ToString(), t.CreatedAt, t.PaidAt);
}

public record IngestTransactionRequest(
    Guid MerchantId,
    string MerchantReference,
    string? PaynowReference,
    decimal Amount,
    string Currency,
    string Status,
    string PaymentMethod,
    DateTime? TransactionDate);
