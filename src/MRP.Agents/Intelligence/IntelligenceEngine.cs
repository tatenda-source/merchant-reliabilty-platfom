using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.Intelligence;

public class IntelligenceEngine : IIntelligenceEngine
{
    private readonly ITransactionRepository _txRepo;
    private readonly IMerchantRepository _merchantRepo;
    private readonly IReconciliationRepository _reconRepo;
    private readonly IRecoveryRepository _recoveryRepo;
    private readonly ISettlementRepository _settlementRepo;
    private readonly IMerchantProfileRepository _profileRepo;
    private readonly IEventBus _eventBus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntelligenceEngine> _logger;

    private readonly ReconciliationEngine _reconEngine = new();
    private readonly AnomalyDetector _anomalyDetector = new();

    private static readonly Dictionary<PaymentMethod, decimal> BaseSettlementHours = new()
    {
        { PaymentMethod.EcoCash, 4m }, { PaymentMethod.OneMoney, 6m },
        { PaymentMethod.Telecash, 8m }, { PaymentMethod.InnBucks, 6m },
        { PaymentMethod.BankTransfer, 24m }, { PaymentMethod.BankCard, 12m },
        { PaymentMethod.ZimSwitch, 24m }
    };

    public IntelligenceEngine(
        ITransactionRepository txRepo, IMerchantRepository merchantRepo,
        IReconciliationRepository reconRepo, IRecoveryRepository recoveryRepo,
        ISettlementRepository settlementRepo, IMerchantProfileRepository profileRepo,
        IEventBus eventBus, IServiceScopeFactory scopeFactory, ILogger<IntelligenceEngine> logger)
    {
        _txRepo = txRepo; _merchantRepo = merchantRepo;
        _reconRepo = reconRepo; _recoveryRepo = recoveryRepo;
        _settlementRepo = settlementRepo; _profileRepo = profileRepo;
        _eventBus = eventBus; _scopeFactory = scopeFactory; _logger = logger;
    }

    // --- Reconciliation Pipeline ---

    public async Task<ReconciliationReport> ReconcileAsync(
        Guid merchantId, DateTime periodStart, DateTime periodEnd, CancellationToken ct)
    {
        var allTx = await _txRepo.GetByMerchantAsync(merchantId, periodStart, periodEnd, ct);

        var paynow = allTx.Where(t => t.Source == SourceType.Paynow).ToList();
        var merchant = allTx.Where(t => t.Source == SourceType.Merchant).ToList();
        var bank = allTx.Where(t => t.Source == SourceType.Bank).ToList();

        var result = _reconEngine.Reconcile(merchantId, paynow, merchant, bank, periodStart, periodEnd);
        await _reconRepo.AddAsync(result.Report, ct);

        // Publish events for each anomaly
        foreach (var anomaly in result.Anomalies)
        {
            await _eventBus.PublishAsync(
                new AnomalyDetected(anomaly.Id, merchantId, anomaly.Type, anomaly.Severity, anomaly.Amount), ct);
        }

        // Also check velocity + duplicates
        var velocityAnomalies = _anomalyDetector.DetectVelocityAnomalies(merchantId, allTx);
        var duplicates = _anomalyDetector.DetectDuplicates(allTx);

        foreach (var anomaly in velocityAnomalies.Concat(duplicates))
        {
            await _recoveryRepo.AddAnomalyAsync(anomaly, ct);
            await _eventBus.PublishAsync(
                new AnomalyDetected(anomaly.Id, merchantId, anomaly.Type, anomaly.Severity, anomaly.Amount), ct);
        }

        await _eventBus.PublishAsync(
            new ReconciliationCompleted(result.Report.Id, merchantId, result.Report.AnomalyCount), ct);

        _logger.LogInformation(
            "Reconciliation for {MerchantId}: {Matched}/{Total} matched, {Anomalies} anomalies",
            merchantId, result.Report.MatchedCount, result.Report.TotalTransactions, result.Report.AnomalyCount);

        return result.Report;
    }

    public async Task<List<ReconciliationReport>> ReconcileBatchAsync(
        IEnumerable<Guid> merchantIds, DateTime periodStart, DateTime periodEnd,
        int maxParallelism, CancellationToken ct)
    {
        var ids = merchantIds.ToList();
        using var semaphore = new SemaphoreSlim(Math.Clamp(maxParallelism, 1, 10));

        // Each merchant reconciliation runs in its own DI scope to avoid sharing
        // a single DbContext across parallel tasks (DbContext is not thread-safe).
        var tasks = ids.Select(async merchantId =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var engine = scope.ServiceProvider.GetRequiredService<IIntelligenceEngine>();
                return await engine.ReconcileAsync(merchantId, periodStart, periodEnd, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Batch reconciliation failed for merchant {MerchantId}", merchantId);
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var reports = await Task.WhenAll(tasks);
        return reports.Where(r => r is not null).ToList()!;
    }

    // --- Merchant Behaviour Analysis ---

    public async Task AnalyseMerchantBehaviourAsync(Guid merchantId, CancellationToken ct)
    {
        var merchant = await _merchantRepo.GetByIdAsync(merchantId, ct);
        if (merchant is null) return;

        var oneHourAgo = DateTime.UtcNow.AddHours(-1);
        var recentTx = await _txRepo.GetByMerchantAsync(merchantId, oneHourAgo, DateTime.UtcNow, ct);

        var oneDayAgo = DateTime.UtcNow.AddDays(-1);
        var dailyTx = await _txRepo.GetByMerchantAsync(merchantId, oneDayAgo, DateTime.UtcNow, ct);

        var txPerHour = (decimal)recentTx.Count;
        var avgTxPerHour = dailyTx.Count / 24m;

        // Detect retries
        var retryGroups = recentTx
            .Where(t => !string.IsNullOrEmpty(t.MerchantReference))
            .GroupBy(t => t.MerchantReference)
            .Where(g => g.Count() > 1).ToList();
        var retryRate = recentTx.Count > 0
            ? retryGroups.Sum(g => g.Count() - 1) / (decimal)recentTx.Count * 100 : 0;

        // Detect duplicates
        var duplicateCount = CountDuplicates(recentTx);
        var duplicateRate = recentTx.Count > 0
            ? duplicateCount / (decimal)recentTx.Count * 100 : 0;

        var callbackFailures = recentTx.Count(t => t.Status == TransactionStatus.Failed);
        var callbackFailureRate = recentTx.Count > 0
            ? callbackFailures / (decimal)recentTx.Count * 100 : 0;

        var riskScore = CalculateBehaviourRisk(txPerHour, avgTxPerHour, retryRate, duplicateRate, callbackFailureRate);

        var alerts = new List<string>();
        if (avgTxPerHour > 0 && txPerHour > avgTxPerHour * 3)
            alerts.Add($"VelocitySpike: {txPerHour}/hr vs avg {avgTxPerHour:F1}/hr");
        if (retryRate > 15) alerts.Add($"HighRetryRate: {retryRate:F1}%");
        if (duplicateRate > 5) alerts.Add($"DuplicateTransactions: {duplicateRate:F1}%");
        if (callbackFailureRate > 10) alerts.Add($"CallbackInstability: {callbackFailureRate:F1}%");

        // Upsert profile
        var profile = await _profileRepo.GetByMerchantAsync(merchantId, ct);
        var isNew = profile is null;
        profile ??= new MerchantProfile { Id = Guid.NewGuid(), MerchantId = merchantId };

        profile.AvgTransactionsPerHour = avgTxPerHour;
        profile.PeakTransactionsPerHour = dailyTx.Count > 0
            ? dailyTx.GroupBy(t => new { t.CreatedAt.Date, t.CreatedAt.Hour }).Max(g => (decimal)g.Count()) : 0;
        profile.RetryRate = retryRate;
        profile.DuplicateRate = duplicateRate;
        profile.CallbackFailureRate = callbackFailureRate;
        profile.BehaviourRiskScore = riskScore;
        profile.ActiveAlerts = alerts.Count > 0 ? JsonSerializer.Serialize(alerts) : null;
        profile.LastAnalysedAt = DateTime.UtcNow;
        profile.UpdatedAt = DateTime.UtcNow;

        if (isNew) await _profileRepo.AddAsync(profile, ct);
        else await _profileRepo.UpdateAsync(profile, ct);

        // Adjust reliability
        if (riskScore > 70 && merchant.ReliabilityScore > 10)
        {
            merchant.ReliabilityScore = Math.Max(0, merchant.ReliabilityScore - Math.Min(riskScore * 0.05m, 5));
            await _merchantRepo.UpdateAsync(merchant, ct);
        }

        await _eventBus.PublishAsync(
            new MerchantRiskUpdated(merchantId, riskScore, merchant.ReliabilityScore), ct);
    }

    // --- Settlement Risk Prediction ---

    public async Task PredictSettlementRiskAsync(Guid transactionId, CancellationToken ct)
    {
        var tx = await _txRepo.GetByIdAsync(transactionId, ct);
        if (tx is null || tx.Status != TransactionStatus.Paid || tx.SettledAt.HasValue) return;

        var merchant = await _merchantRepo.GetByIdAsync(tx.MerchantId, ct);
        if (merchant is null) return;

        var riskScore = CalculateSettlementRisk(tx, merchant);
        var predictedHours = PredictSettlementHours(tx.PaymentMethod, merchant.ReliabilityScore);

        var settlement = new Settlement
        {
            Id = Guid.NewGuid(),
            TransactionId = tx.Id,
            MerchantId = tx.MerchantId,
            Amount = tx.Amount,
            PaymentMethod = tx.PaymentMethod,
            RiskScore = riskScore,
            Confidence = CalculateConfidence(merchant.ReliabilityScore, tx.PaymentMethod),
            PredictedSettlementTime = tx.PaidAt!.Value.AddHours((double)predictedHours),
            RiskFactors = JsonSerializer.Serialize(BuildRiskFactors(tx, merchant)),
            CreatedAt = DateTime.UtcNow
        };

        await _settlementRepo.AddAsync(settlement, ct);

        if (riskScore >= 70)
        {
            await _eventBus.PublishAsync(
                new SettlementRiskDetected(settlement.Id, tx.Id, tx.MerchantId, riskScore), ct);
        }
    }

    // --- Private helpers ---

    private static decimal CalculateBehaviourRisk(
        decimal currentTxPerHour, decimal avgTxPerHour,
        decimal retryRate, decimal duplicateRate, decimal callbackFailureRate)
    {
        decimal risk = 0;
        if (avgTxPerHour > 0)
        {
            var ratio = currentTxPerHour / avgTxPerHour;
            if (ratio > 3) risk += Math.Min((ratio - 1) * 10, 35);
        }
        if (retryRate > 15) risk += Math.Min(retryRate * 1.5m, 25);
        if (duplicateRate > 5) risk += Math.Min(duplicateRate * 3, 20);
        if (callbackFailureRate > 10) risk += Math.Min(callbackFailureRate * 2, 20);
        return Math.Clamp(risk, 0, 100);
    }

    private static decimal CalculateSettlementRisk(Transaction tx, Merchant merchant)
    {
        decimal risk = (100 - merchant.ReliabilityScore) * 0.3m;
        risk += tx.PaymentMethod switch
        {
            PaymentMethod.BankTransfer => 20, PaymentMethod.ZimSwitch => 18,
            PaymentMethod.BankCard => 12, PaymentMethod.Telecash => 10,
            PaymentMethod.OneMoney => 8, PaymentMethod.InnBucks => 8,
            PaymentMethod.EcoCash => 5, _ => 10
        };
        if (tx.PaidAt.HasValue)
        {
            var hoursSince = (decimal)(DateTime.UtcNow - tx.PaidAt.Value).TotalHours;
            var expected = BaseSettlementHours.GetValueOrDefault(tx.PaymentMethod, 12m);
            if (hoursSince > expected) risk += Math.Min((hoursSince - expected) * 3, 30);
        }
        if (DateTime.UtcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) risk += 10;
        if (DateTime.UtcNow.Hour < 8 || DateTime.UtcNow.Hour > 17) risk += 5;
        if (tx.Amount > 500) risk += 8;
        if (tx.Amount > 1000) risk += 7;
        return Math.Clamp(risk, 0, 100);
    }

    private static decimal PredictSettlementHours(PaymentMethod method, decimal reliability)
        => BaseSettlementHours.GetValueOrDefault(method, 12m) * (1 + (100 - reliability) / 200);

    private static decimal CalculateConfidence(decimal reliability, PaymentMethod method)
    {
        var baseConf = 50 + reliability * 0.4m;
        var bonus = method switch
        {
            PaymentMethod.EcoCash => 10, PaymentMethod.OneMoney => 8,
            PaymentMethod.InnBucks => 8, PaymentMethod.Telecash => 6, _ => 0
        };
        return Math.Clamp(baseConf + bonus, 0, 100);
    }

    private static List<string> BuildRiskFactors(Transaction tx, Merchant merchant)
    {
        var factors = new List<string>();
        if (merchant.ReliabilityScore < 60) factors.Add($"Low reliability: {merchant.ReliabilityScore}%");
        if (tx.PaymentMethod is PaymentMethod.BankTransfer or PaymentMethod.ZimSwitch)
            factors.Add($"Slow method: {tx.PaymentMethod}");
        if (tx.Amount > 500) factors.Add($"High value: ${tx.Amount}");
        if (DateTime.UtcNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            factors.Add("Weekend processing");
        return factors;
    }

    private static int CountDuplicates(List<Transaction> transactions)
    {
        var duplicates = 0;
        foreach (var group in transactions
            .Where(t => !string.IsNullOrEmpty(t.MerchantReference))
            .GroupBy(t => new { t.MerchantReference, t.Amount }))
        {
            var ordered = group.OrderBy(t => t.CreatedAt).ToList();
            for (var i = 1; i < ordered.Count; i++)
                if ((ordered[i].CreatedAt - ordered[i - 1].CreatedAt).TotalMinutes <= 5)
                    duplicates++;
        }
        return duplicates;
    }
}
