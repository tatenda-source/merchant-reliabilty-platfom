using Microsoft.AspNetCore.Mvc;
using MRP.Application.DTOs;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Interfaces;

namespace MRP.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MerchantsController : ControllerBase
{
    private readonly IMerchantRepository _merchantRepo;
    private readonly IServiceScopeFactory _scopeFactory;

    public MerchantsController(IMerchantRepository merchantRepo, IServiceScopeFactory scopeFactory)
    {
        _merchantRepo = merchantRepo;
        _scopeFactory = scopeFactory;
    }

    [HttpGet]
    public async Task<ActionResult<List<MerchantDto>>> GetAll(
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var merchants = await _merchantRepo.GetAllAsync(page, pageSize, ct);
        return Ok(merchants.Select(MapToDto).ToList());
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MerchantDto>> GetById(Guid id, CancellationToken ct)
    {
        var merchant = await _merchantRepo.GetByIdAsync(id, ct);
        if (merchant is null) return NotFound();
        return Ok(MapToDto(merchant));
    }

    [HttpPost]
    public async Task<ActionResult<MerchantDto>> Create(
        [FromBody] CreateMerchantRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrWhiteSpace(request.ContactEmail))
            return BadRequest(new { error = "ContactEmail is required" });
        if (string.IsNullOrWhiteSpace(request.PaynowIntegrationId))
            return BadRequest(new { error = "PaynowIntegrationId is required" });
        if (string.IsNullOrWhiteSpace(request.PaynowIntegrationKey))
            return BadRequest(new { error = "PaynowIntegrationKey is required" });
        if (!Enum.TryParse<MerchantTier>(request.Tier, ignoreCase: true, out var tier))
            tier = MerchantTier.Standard;

        var merchant = new Merchant
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TradingName = request.TradingName,
            ContactEmail = request.ContactEmail,
            ContactPhone = request.ContactPhone,
            Tier = tier,
            IsActive = false,
            OnboardedAt = DateTime.UtcNow,
            ReliabilityScore = 50,
            Integration = new MerchantIntegration
            {
                Id = Guid.NewGuid(),
                PaynowIntegrationId = request.PaynowIntegrationId,
                PaynowIntegrationKey = request.PaynowIntegrationKey,
                ResultUrl = request.ResultUrl,
                ReturnUrl = request.ReturnUrl
            }
        };

        await _merchantRepo.AddAsync(merchant, ct);

        // Validate integration in a NEW scope to avoid using disposed scoped services
        var merchantId = merchant.Id;
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<IIngestionService>();
                await ingestion.ValidateMerchantIntegrationAsync(merchantId, CancellationToken.None);
            }
            catch (Exception)
            {
                // Logged inside ValidateMerchantIntegrationAsync
            }
        });

        return CreatedAtAction(nameof(GetById), new { id = merchant.Id }, MapToDto(merchant));
    }

    [HttpGet("{id:guid}/health")]
    public async Task<ActionResult> GetHealth(Guid id, CancellationToken ct)
    {
        var merchant = await _merchantRepo.GetByIdAsync(id, ct);
        if (merchant is null) return NotFound();

        return Ok(new
        {
            merchantId = merchant.Id,
            reliabilityScore = merchant.ReliabilityScore,
            isActive = merchant.IsActive,
            integration = merchant.Integration is not null ? new
            {
                merchant.Integration.IsCallbackReachable,
                merchant.Integration.LastCallbackTestAt,
                merchant.Integration.ConsecutiveFailures
            } : null
        });
    }

    private static MerchantDto MapToDto(Merchant m) => new(
        m.Id, m.Name, m.TradingName, m.ContactEmail,
        m.Tier.ToString(), m.IsActive, m.ReliabilityScore,
        m.OnboardedAt, m.LastActivityAt,
        m.Integration is not null ? new MerchantIntegrationDto(
            m.Integration.IsCallbackReachable,
            m.Integration.LastCallbackTestAt,
            m.Integration.LastTransactionAt,
            m.Integration.ConsecutiveFailures) : null);
}
