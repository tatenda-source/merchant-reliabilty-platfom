using MRP.Domain.Enums;

namespace MRP.Domain.Events;

// Ingestion events
public record TransactionReceived(Guid TransactionId, Guid MerchantId, SourceType Source, decimal Amount);
public record BankSettlementReceived(Guid TransactionId, Guid MerchantId, decimal Amount);
public record MerchantCreated(Guid MerchantId);

// Intelligence events
public record AnomalyDetected(Guid AnomalyId, Guid MerchantId, AnomalyType Type, string Severity, decimal? Amount);
public record ReconciliationCompleted(Guid ReportId, Guid MerchantId, int AnomalyCount);
public record MerchantRiskUpdated(Guid MerchantId, decimal BehaviourRiskScore, decimal ReliabilityScore);
public record SettlementRiskDetected(Guid SettlementId, Guid TransactionId, Guid MerchantId, decimal RiskScore);

// Recovery events
public record RecoveryCompleted(Guid RecoveryAttemptId, Guid AnomalyId, bool WasSuccessful, RecoveryStrategy Strategy);
