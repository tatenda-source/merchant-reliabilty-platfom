using MRP.Domain.Enums;

namespace MRP.Domain.Events;

public record TransactionIngested(Guid TransactionId, Guid MerchantId, SourceType Source);
public record AnomalyDetected(Guid AnomalyId, Guid MerchantId, AnomalyType Type, string Severity);
public record ReconciliationCompleted(Guid ReportId, Guid MerchantId, int AnomalyCount);
public record RecoveryInitiated(Guid RecoveryAttemptId, Guid AnomalyId, RecoveryStrategy Strategy);
public record RecoveryCompleted(Guid RecoveryAttemptId, bool WasSuccessful);
public record MerchantOnboarded(Guid MerchantId, bool IntegrationValid);
public record SettlementRiskDetected(Guid PredictionId, Guid TransactionId, Guid MerchantId, decimal RiskScore);
public record MerchantBehaviourAlert(Guid MerchantId, string AlertType, decimal RiskScore, string Details);
public record RecoveryStrategyDecided(Guid DecisionId, Guid AnomalyId, RecoveryStrategy ChosenStrategy, decimal Confidence);
