using Microsoft.AspNetCore.Mvc;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly ITransactionRepository _txRepo;
    private readonly IEventBus _eventBus;

    public WebhooksController(ITransactionRepository txRepo, IEventBus eventBus)
    {
        _txRepo = txRepo;
        _eventBus = eventBus;
    }

    [HttpPost("paynow")]
    public async Task<IActionResult> PaynowCallback(
        [FromForm] string reference,
        [FromForm] decimal amount,
        [FromForm] string status,
        [FromForm] string? pollurl,
        [FromForm] string? paynowreference,
        [FromForm] string? hash,
        CancellationToken ct)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            PaynowReference = paynowreference ?? reference,
            MerchantReference = reference,
            Amount = amount,
            Currency = "USD",
            Status = status?.ToLower() switch
            {
                "paid" => TransactionStatus.Paid,
                "cancelled" => TransactionStatus.Cancelled,
                "failed" => TransactionStatus.Failed,
                _ => TransactionStatus.Pending
            },
            Source = SourceType.Paynow,
            CreatedAt = DateTime.UtcNow,
            PaidAt = status?.ToLower() == "paid" ? DateTime.UtcNow : null,
            RawPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reference, amount, status, pollurl, paynowreference, hash
            })
        };

        await _txRepo.AddAsync(transaction, ct);
        await _eventBus.PublishAsync(
            new Domain.Events.TransactionIngested(transaction.Id, transaction.MerchantId, SourceType.Paynow), ct);

        return Ok();
    }
}
