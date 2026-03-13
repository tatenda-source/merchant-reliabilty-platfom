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
    decimal ConfidenceScore,
    string? DecisionReason,
    DateTime AttemptedAt,
    DateTime? CompletedAt);

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
    string? RiskFactors,
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
    decimal AvgTransactionsPerHour,
    decimal PeakTransactionsPerHour,
    decimal RetryRate,
    decimal DuplicateRate,
    decimal CallbackFailureRate,
    decimal BehaviourRiskScore,
    List<string>? ActiveAlerts,
    DateTime LastAnalysedAt);

// Recovery Intelligence DTOs
public record RecoveryIntelligenceStatsDto(
    int TotalAttempts,
    decimal SuccessRate,
    decimal AverageConfidence);

// Intelligence request DTOs
public record TriggerSettlementAnalysisRequest(Guid MerchantId);
public record TriggerBehaviourAnalysisRequest(Guid MerchantId);
