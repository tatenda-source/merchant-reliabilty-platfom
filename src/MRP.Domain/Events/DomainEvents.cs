using MRP.Domain.Enums;

namespace MRP.Domain.Events;

public record TransactionIngested(Guid TransactionId, Guid MerchantId, SourceType Source);
public record AnomalyDetected(Guid AnomalyId, Guid MerchantId, AnomalyType Type, string Severity);
public record ReconciliationCompleted(Guid ReportId, Guid MerchantId, int AnomalyCount);
public record RecoveryInitiated(Guid RecoveryAttemptId, Guid AnomalyId, RecoveryStrategy Strategy);
public record RecoveryCompleted(Guid RecoveryAttemptId, bool WasSuccessful);
public record MerchantOnboarded(Guid MerchantId, bool IntegrationValid);
