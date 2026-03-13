using Microsoft.AspNetCore.Mvc;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly IIngestionService _ingestion;

    public WebhooksController(IIngestionService ingestion) => _ingestion = ingestion;

    [HttpPost("paynow")]
    public async Task<IActionResult> PaynowCallback(
        [FromForm] string reference,
        [FromForm] decimal amount,
        [FromForm] string status,
        [FromForm] string? pollurl,
        [FromForm] string? paynowreference,
        [FromForm] string? hash,
        [FromForm] Guid? merchantId,
        CancellationToken ct)
    {
        await _ingestion.IngestPaynowWebhookAsync(
            reference, amount, status, pollurl, paynowreference,
            merchantId ?? Guid.Empty, ct);

        return Ok();
    }
}
