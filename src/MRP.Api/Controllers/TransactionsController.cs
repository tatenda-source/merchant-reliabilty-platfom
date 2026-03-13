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

    public TransactionsController(ITransactionRepository txRepo)
    {
        _txRepo = txRepo;
    }

    [HttpGet]
    public async Task<ActionResult<List<TransactionDto>>> GetByMerchant(
        [FromQuery] Guid merchantId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct)
    {
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
    public async Task<ActionResult> IngestBatch(
        [FromBody] List<IngestTransactionRequest> requests, CancellationToken ct)
    {
        var transactions = requests.Select(r => new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = r.MerchantId,
            PaynowReference = r.PaynowReference ?? string.Empty,
            MerchantReference = r.MerchantReference,
            Amount = r.Amount,
            Currency = r.Currency,
            Status = Enum.TryParse<TransactionStatus>(r.Status, out var s)
                ? s : TransactionStatus.Pending,
            Source = SourceType.Merchant,
            PaymentMethod = Enum.TryParse<PaymentMethod>(r.PaymentMethod, out var pm)
                ? pm : PaymentMethod.EcoCash,
            CreatedAt = r.TransactionDate ?? DateTime.UtcNow
        });

        await _txRepo.AddRangeAsync(transactions, ct);
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
