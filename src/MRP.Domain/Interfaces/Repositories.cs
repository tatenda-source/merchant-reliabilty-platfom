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
    Task<RecoveryAttempt> AddAttemptAsync(RecoveryAttempt attempt, CancellationToken ct);
    Task<List<RecoveryAttempt>> GetAttemptsByAnomalyAsync(Guid anomalyId, CancellationToken ct);
}

public interface IAgentTaskRepository
{
    Task<AgentTask?> DequeueAsync(AgentType agentType, CancellationToken ct);
    Task<AgentTask> AddAsync(AgentTask task, CancellationToken ct);
    Task UpdateAsync(AgentTask task, CancellationToken ct);
    Task SaveResultAsync(AgentResult result, CancellationToken ct);
}
