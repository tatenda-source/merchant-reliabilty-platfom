namespace MRP.Application.DTOs;

public record DashboardMetricsDto(
    decimal ReliabilityScore,
    int TotalTransactions,
    int MatchedTransactions,
    int FailedTransactions,
    int ActiveMerchants,
    int AnomaliesDetected,
    decimal RecoveryRate,
    decimal TotalVolume,
    decimal RecoveredAmount);

public record MerchantDto(
    Guid Id,
    string Name,
    string TradingName,
    string ContactEmail,
    string Tier,
    bool IsActive,
    decimal ReliabilityScore,
    DateTime OnboardedAt,
    DateTime? LastActivityAt,
    MerchantIntegrationDto? Integration);

public record MerchantIntegrationDto(
    bool IsCallbackReachable,
    DateTime? LastCallbackTestAt,
    DateTime? LastTransactionAt,
    int ConsecutiveFailures);

public record TransactionDto(
    Guid Id,
    Guid MerchantId,
    string PaynowReference,
    string MerchantReference,
    decimal Amount,
    string Currency,
    string Status,
    string Source,
    string PaymentMethod,
    DateTime CreatedAt,
    DateTime? PaidAt);

public record ReconciliationReportDto(
    Guid Id,
    Guid MerchantId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    DateTime GeneratedAt,
    int TotalTransactions,
    int MatchedCount,
    int UnmatchedCount,
    int AnomalyCount,
    decimal TotalVolume,
    decimal MatchedVolume,
    decimal DiscrepancyVolume,
    decimal MatchRate);

public record AnomalyDto(
    Guid Id,
    string Type,
    string Description,
    string Severity,
    bool IsResolved,
    DateTime DetectedAt,
    DateTime? ResolvedAt);

public record RecoveryAttemptDto(
    Guid Id,
    Guid AnomalyId,
    string Strategy,
    int AttemptNumber,
    bool IsSuccessful,
    string? ResultDetails,
    DateTime AttemptedAt,
    DateTime? CompletedAt);

public record AgentStatusDto(
    string Name,
    string Type,
    string State,
    DateTime? LastRunAt,
    int PendingTasks,
    int CompletedTasks,
    decimal SuccessRate);

public record CreateMerchantRequest(
    string Name,
    string TradingName,
    string ContactEmail,
    string ContactPhone,
    string Tier,
    string PaynowIntegrationId,
    string PaynowIntegrationKey,
    string ResultUrl,
    string ReturnUrl);

public record TriggerReconciliationRequest(
    Guid MerchantId,
    DateTime? PeriodStart,
    DateTime? PeriodEnd);

public record InitiateRecoveryRequest(
    Guid AnomalyId);

// Settlement Intelligence DTOs
public record SettlementPredictionDto(
    Guid Id,
    Guid TransactionId,
    Guid MerchantId,
    decimal RiskScore,
    DateTime PredictedSettlementTime,
    decimal Confidence,
    string PaymentMethod,
    string RiskFactors,
    DateTime CreatedAt,
    DateTime? ActualSettlementTime);

public record SettlementRiskSummaryDto(
    int TotalPredictions,
    int HighRiskCount,
    decimal AverageRiskScore,
    decimal PredictionAccuracy);

// Merchant Behaviour DTOs
public record MerchantBehaviourDto(
    Guid MerchantId,
    decimal AvgTransactionsPerMinute,
    decimal AvgTransactionsPerHour,
    decimal RetryRate,
    decimal DuplicateRate,
    decimal CallbackFailureRate,
    decimal RiskScore,
    decimal PeakTransactionsPerHour,
    List<string>? ActiveAlerts,
    DateTime LastAnalysedAt);

// Recovery Intelligence DTOs
public record RecoveryStrategyDecisionDto(
    Guid Id,
    Guid AnomalyId,
    string ChosenStrategy,
    decimal ConfidenceScore,
    string DecisionReason,
    decimal MerchantReliabilityAtDecision,
    decimal FinancialRiskAmount,
    bool WasEffective,
    DateTime DecidedAt);

public record RecoveryIntelligenceStatsDto(
    int TotalDecisions,
    decimal AutonomousRecoveryRate,
    decimal AverageConfidence,
    Dictionary<string, decimal> StrategySuccessRates);

// Intelligence request DTOs
public record TriggerSettlementAnalysisRequest(Guid MerchantId);
public record TriggerBehaviourAnalysisRequest(Guid MerchantId);
public record TriggerIntelligentRecoveryRequest(Guid AnomalyId, Guid MerchantId, decimal TransactionAmount);
