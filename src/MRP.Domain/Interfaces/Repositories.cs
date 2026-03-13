using MRP.Domain.Entities;
using MRP.Domain.Enums;

namespace MRP.Domain.Interfaces;

public interface IMerchantRepository
{
    Task<Merchant?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Merchant>> GetAllAsync(int page, int pageSize, CancellationToken ct);
    Task<Merchant> AddAsync(Merchant merchant, CancellationToken ct);
    Task UpdateAsync(Merchant merchant, CancellationToken ct);
}

public interface ITransactionRepository
{
    Task<Transaction?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<Transaction>> GetByMerchantAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken ct);
    Task<List<Transaction>> GetPendingAsync(SourceType source, CancellationToken ct);
    Task<Transaction> AddAsync(Transaction transaction, CancellationToken ct);
    Task AddRangeAsync(IEnumerable<Transaction> transactions, CancellationToken ct);
    Task UpdateAsync(Transaction transaction, CancellationToken ct);
}

public interface IReconciliationRepository
{
    Task<ReconciliationReport?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<List<ReconciliationReport>> GetByMerchantAsync(Guid merchantId, CancellationToken ct);
    Task<ReconciliationReport> AddAsync(ReconciliationReport report, CancellationToken ct);
}

public interface IRecoveryRepository
{
    Task<List<Anomaly>> GetUnresolvedAnomaliesAsync(CancellationToken ct);
    Task<Anomaly?> GetAnomalyByIdAsync(Guid id, CancellationToken ct);
    Task<RecoveryAttempt> AddAttemptAsync(RecoveryAttempt attempt, CancellationToken ct);
    Task<List<RecoveryAttempt>> GetAttemptsByAnomalyAsync(Guid anomalyId, CancellationToken ct);
    Task AddAnomalyAsync(Anomaly anomaly, CancellationToken ct);
    Task UpdateAnomalyAsync(Anomaly anomaly, CancellationToken ct);
    Task<Dictionary<RecoveryStrategy, decimal>> GetStrategySuccessRatesAsync(AnomalyType anomalyType, CancellationToken ct);
}

public interface ISettlementRepository
{
    Task<Settlement> AddAsync(Settlement settlement, CancellationToken ct);
    Task<List<Settlement>> GetByMerchantAsync(Guid merchantId, CancellationToken ct);
    Task<List<Settlement>> GetHighRiskAsync(decimal threshold, CancellationToken ct);
    Task UpdateAsync(Settlement settlement, CancellationToken ct);
}

public interface IMerchantProfileRepository
{
    Task<MerchantProfile?> GetByMerchantAsync(Guid merchantId, CancellationToken ct);
    Task<MerchantProfile> AddAsync(MerchantProfile profile, CancellationToken ct);
    Task UpdateAsync(MerchantProfile profile, CancellationToken ct);
    Task<List<MerchantProfile>> GetHighRiskAsync(decimal threshold, CancellationToken ct);
}
