using Microsoft.Extensions.Logging;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Ingestion;

public class IngestionService : IIngestionService
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMerchantRepository _merchantRepo;
    private readonly IPaynowGateway _paynow;
    private readonly IEventBus _eventBus;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IngestionService> _logger;

    public IngestionService(
        ITransactionRepository txRepo,
        IMerchantRepository merchantRepo,
        IPaynowGateway paynow,
        IEventBus eventBus,
        IHttpClientFactory httpClientFactory,
        ILogger<IngestionService> logger)
    {
        _txRepo = txRepo;
        _merchantRepo = merchantRepo;
        _paynow = paynow;
        _eventBus = eventBus;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<Transaction> IngestPaynowWebhookAsync(
        string reference, decimal amount, string status,
        string? pollUrl, string? paynowReference, Guid merchantId,
        CancellationToken ct)
    {
        var transaction = new Transaction
        {
            Id = Guid.NewGuid(),
            MerchantId = merchantId,
            PaynowReference = paynowReference ?? reference,
            MerchantReference = reference,
            Amount = amount,
            Currency = "USD",
            Status = status.ToLower() switch
            {
                "paid" => TransactionStatus.Paid,
                "cancelled" => TransactionStatus.Cancelled,
                "failed" => TransactionStatus.Failed,
                _ => TransactionStatus.Pending
            },
            Source = SourceType.Paynow,
            CreatedAt = DateTime.UtcNow,
            PaidAt = status.ToLower() == "paid" ? DateTime.UtcNow : null,
            RawPayload = System.Text.Json.JsonSerializer.Serialize(new
            {
                reference, amount, status, pollUrl, paynowReference
            })
        };

        await _txRepo.AddAsync(transaction, ct);

        _logger.LogInformation("Ingested Paynow transaction {Reference} amount={Amount}",
            reference, amount);

        await _eventBus.PublishAsync(
            new TransactionReceived(transaction.Id, merchantId, SourceType.Paynow, amount), ct);

        return transaction;
    }

    public async Task IngestMerchantBatchAsync(IEnumerable<Transaction> transactions, CancellationToken ct)
    {
        var txList = transactions.ToList();
        await _txRepo.AddRangeAsync(txList, ct);

        foreach (var tx in txList)
        {
            await _eventBus.PublishAsync(
                new TransactionReceived(tx.Id, tx.MerchantId, SourceType.Merchant, tx.Amount), ct);
        }

        _logger.LogInformation("Ingested {Count} merchant transactions", txList.Count);
    }

    public async Task ValidateMerchantIntegrationAsync(Guid merchantId, CancellationToken ct)
    {
        var merchant = await _merchantRepo.GetByIdAsync(merchantId, ct);
        if (merchant?.Integration is null)
        {
            _logger.LogWarning("Merchant {MerchantId} or integration not found", merchantId);
            return;
        }

        var issues = new List<string>();
        var integration = merchant.Integration;

        // Validate credentials
        if (string.IsNullOrWhiteSpace(integration.PaynowIntegrationId))
            issues.Add("Missing Paynow Integration ID");
        if (string.IsNullOrWhiteSpace(integration.PaynowIntegrationKey))
            issues.Add("Missing Paynow Integration Key");

        // Validate URLs
        if (!Uri.TryCreate(integration.ResultUrl, UriKind.Absolute, out var resultUri)
            || resultUri.Scheme != "https")
            issues.Add("Result URL must be a valid HTTPS URL");

        // Test callback reachability (IHttpClientFactory avoids socket exhaustion)
        bool callbackReachable = false;
        try
        {
            using var http = _httpClientFactory.CreateClient("CallbackTest");
            var response = await http.GetAsync(integration.ResultUrl, ct);
            callbackReachable = response.IsSuccessStatusCode
                || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed;
        }
        catch
        {
            issues.Add("Callback URL is not reachable");
        }

        // Test payment initiation
        bool paymentTestPassed = false;
        if (issues.Count == 0)
        {
            try
            {
                var testResult = await _paynow.InitiatePaymentAsync(
                    integration, $"MRP-TEST-{Guid.NewGuid():N}", 0.01m,
                    "test@mrp.local", ct);
                paymentTestPassed = testResult.Success;
                if (!testResult.Success)
                    issues.Add($"Test payment failed: {testResult.Error}");
            }
            catch (Exception ex)
            {
                issues.Add($"Payment test exception: {ex.Message}");
            }
        }

        // Calculate health score
        int score = 100;
        score -= issues.Count * 15;
        if (!callbackReachable) score -= 20;
        if (!paymentTestPassed) score -= 25;
        score = Math.Clamp(score, 0, 100);

        integration.IsCallbackReachable = callbackReachable;
        integration.LastCallbackTestAt = DateTime.UtcNow;
        integration.ConsecutiveFailures = issues.Count > 0
            ? integration.ConsecutiveFailures + 1 : 0;

        merchant.ReliabilityScore = score;
        merchant.IsActive = issues.Count == 0;
        await _merchantRepo.UpdateAsync(merchant, ct);

        _logger.LogInformation("Validated merchant {MerchantId}: score={Score}, issues={Issues}",
            merchantId, score, issues.Count);

        // Only publish MerchantCreated when validation succeeds
        if (issues.Count == 0)
        {
            await _eventBus.PublishAsync(new MerchantCreated(merchantId), ct);
        }
    }
}
