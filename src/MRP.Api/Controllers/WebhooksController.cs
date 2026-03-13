using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhooksController : ControllerBase
{
    private readonly IIngestionService _ingestion;
    private readonly IMerchantRepository _merchantRepo;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IIngestionService ingestion,
        IMerchantRepository merchantRepo,
        ILogger<WebhooksController> logger)
    {
        _ingestion = ingestion;
        _merchantRepo = merchantRepo;
        _logger = logger;
    }

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
        // Validate required fields
        if (string.IsNullOrWhiteSpace(reference))
            return BadRequest(new { error = "Reference is required" });
        if (amount < 0)
            return BadRequest(new { error = "Amount cannot be negative" });
        if (string.IsNullOrWhiteSpace(status))
            return BadRequest(new { error = "Status is required" });

        // Validate merchantId — reject Guid.Empty
        if (!merchantId.HasValue || merchantId.Value == Guid.Empty)
        {
            _logger.LogWarning("Paynow webhook received without valid merchantId for reference {Reference}", reference);
            return BadRequest(new { error = "Valid merchantId is required" });
        }

        // Validate Paynow hash for webhook integrity
        var merchant = await _merchantRepo.GetByIdAsync(merchantId.Value, ct);
        if (merchant?.Integration is null)
        {
            _logger.LogWarning("Paynow webhook for unknown merchant {MerchantId}", merchantId);
            return BadRequest(new { error = "Unknown merchant" });
        }

        if (!string.IsNullOrEmpty(hash))
        {
            var payload = $"{reference}{amount}{status}{pollurl ?? ""}{paynowreference ?? ""}";
            var expectedHash = ComputeHash(payload, merchant.Integration.PaynowIntegrationKey);
            if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid Paynow webhook hash for reference {Reference}", reference);
                return Unauthorized(new { error = "Invalid webhook hash" });
            }
        }

        await _ingestion.IngestPaynowWebhookAsync(
            reference, amount, status, pollurl, paynowreference,
            merchantId.Value, ct);

        return Ok();
    }

    private static string ComputeHash(string payload, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexStringLower(hashBytes);
    }
}
