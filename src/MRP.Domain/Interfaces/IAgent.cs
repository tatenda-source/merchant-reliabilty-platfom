using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Domain.Interfaces;

public interface IIngestionService
{
    Task<Transaction> IngestPaynowWebhookAsync(string reference, decimal amount, string status,
        string? pollUrl, string? paynowReference, Guid merchantId, CancellationToken ct);
    Task IngestMerchantBatchAsync(IEnumerable<Transaction> transactions, CancellationToken ct);
    Task ValidateMerchantIntegrationAsync(Guid merchantId, CancellationToken ct);
}

public interface IIntelligenceEngine
{
    Task<ReconciliationReport> ReconcileAsync(Guid merchantId, DateTime periodStart, DateTime periodEnd, CancellationToken ct);
    Task AnalyseMerchantBehaviourAsync(Guid merchantId, CancellationToken ct);
    Task PredictSettlementRiskAsync(Guid transactionId, CancellationToken ct);
}

public interface IRecoveryEngine
{
    Task<RecoveryAttempt> RecoverAsync(Guid anomalyId, CancellationToken ct);
}
