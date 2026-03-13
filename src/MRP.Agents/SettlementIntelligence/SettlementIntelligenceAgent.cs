using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MRP.Agents.Core;
using MRP.Domain.Entities;
using MRP.Domain.Enums;
using MRP.Domain.Events;
using MRP.Domain.Interfaces;

namespace MRP.Agents.SettlementIntelligence;

public class SettlementIntelligenceAgent : AgentBase
{
    public override string Name => "SettlementIntelligenceAgent";
    public override AgentType Type => AgentType.SettlementIntelligence;

    private static readonly Dictionary<PaymentMethod, decimal> BaseSettlementHours = new()
    {
        { PaymentMethod.EcoCash, 4m },
        { PaymentMethod.OneMoney, 6m },
        { PaymentMethod.Telecash, 8m },
        { PaymentMethod.InnBucks, 6m },
        { PaymentMethod.BankTransfer, 24m },
        { PaymentMethod.BankCard, 12m },
        { PaymentMethod.ZimSwitch, 24m }
    };

    public SettlementIntelligenceAgent(
        IServiceScopeFactory scopeFactory,
        ILogger<SettlementIntelligenceAgent> logger)
        : base(scopeFactory, logger, TimeSpan.FromMinutes(10))
    { }

    public override async Task<AgentResult> ExecuteAsync(AgentTask task, CancellationToken ct)
    {
        using var scope = CreateScope();
        var txRepo = scope.ServiceProvider.GetRequiredService<ITransactionRepository>();
        var merchantRepo = scope.ServiceProvider.GetRequiredService<IMerchantRepository>();
        var settlementRepo = scope.ServiceProvider.GetRequiredService<ISettlementRepository>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var input = JsonSerializer.Deserialize<SettlementAnalysisInput>(task.InputPayload ?? "{}");
        var merchantId = input?.MerchantId ?? Guid.Empty;

        var merchant = await merchantRepo.GetByIdAsync(merchantId, ct);
        if (merchant is null)
        {
            return new AgentResult
            {
                Id = Guid.NewGuid(),
                AgentTaskId = task.Id,
                IsSuccess = false,
                ErrorMessage = $"Merchant {merchantId} not found",
                CompletedAt = DateTime.UtcNow
            };
        }

        // Fetch recent paid but unsettled transactions
        var lookbackStart = DateTime.UtcNow.AddHours(-72);
        var transactions = await txRepo.GetByMerchantAsync(merchantId, lookbackStart, DateTime.UtcNow, ct);
        var unsettled = transactions
            .Where(t => t.Status == TransactionStatus.Paid && t.SettledAt == null)
            .ToList();

        var predictions = new List<SettlementPrediction>();
        var highRiskCount = 0;

        foreach (var tx in unsettled)
        {
            var riskScore = CalculateSettlementRisk(tx, merchant);
            var predictedHours = PredictSettlementTime(tx.PaymentMethod, merchant.ReliabilityScore);
            var riskFactors = BuildRiskFactors(tx, merchant);

            var prediction = new SettlementPrediction
            {
                Id = Guid.NewGuid(),
                TransactionId = tx.Id,
                MerchantId = merchantId,
                RiskScore = riskScore,
                PredictedSettlementTime = tx.PaidAt!.Value.AddHours((double)predictedHours),
                Confidence = CalculateConfidence(merchant.ReliabilityScore, tx.PaymentMethod),
                PaymentMethod = tx.PaymentMethod,
                RiskFactors = JsonSerializer.Serialize(riskFactors),
                CreatedAt = DateTime.UtcNow
            };

            await settlementRepo.AddAsync(prediction, ct);
            predictions.Add(prediction);

            if (riskScore >= 70)
            {
                highRiskCount++;
                await eventBus.PublishAsync(
                    new SettlementRiskDetected(prediction.Id, tx.Id, merchantId, riskScore), ct);
            }
        }

        return new AgentResult
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            IsSuccess = true,
            OutputPayload = JsonSerializer.Serialize(new
            {
                merchantId,
                transactionsAnalysed = unsettled.Count,
                predictionsGenerated = predictions.Count,
                highRiskCount,
                averageRiskScore = predictions.Count > 0
                    ? Math.Round(predictions.Average(p => p.RiskScore), 1)
                    : 0m
            }),
            CompletedAt = DateTime.UtcNow
        };
    }

    private static decimal CalculateSettlementRisk(Transaction tx, Merchant merchant)
    {
        decimal risk = 0;

        // Merchant reliability inversely affects risk
        risk += (100 - merchant.ReliabilityScore) * 0.3m;

        // Payment method risk
        risk += tx.PaymentMethod switch
        {
            PaymentMethod.BankTransfer => 20,
            PaymentMethod.ZimSwitch => 18,
            PaymentMethod.BankCard => 12,
            PaymentMethod.Telecash => 10,
            PaymentMethod.OneMoney => 8,
            PaymentMethod.InnBucks => 8,
            PaymentMethod.EcoCash => 5,
            _ => 10
        };

        // Time since payment — longer wait = higher risk
        if (tx.PaidAt.HasValue)
        {
            var hoursSincePaid = (decimal)(DateTime.UtcNow - tx.PaidAt.Value).TotalHours;
            var expectedHours = BaseSettlementHours.GetValueOrDefault(tx.PaymentMethod, 12m);

            if (hoursSincePaid > expectedHours)
            {
                risk += Math.Min((hoursSincePaid - expectedHours) * 3, 30);
            }
        }

        // Weekend/off-hours risk
        var now = DateTime.UtcNow;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            risk += 10;
        if (now.Hour < 8 || now.Hour > 17)
            risk += 5;

        // High-value transaction risk
        if (tx.Amount > 500)
            risk += 8;
        if (tx.Amount > 1000)
            risk += 7;

        return Math.Clamp(risk, 0, 100);
    }

    private static decimal PredictSettlementTime(PaymentMethod method, decimal merchantReliability)
    {
        var baseHours = BaseSettlementHours.GetValueOrDefault(method, 12m);

        // Less reliable merchants see longer settlement times
        var reliabilityFactor = 1 + (100 - merchantReliability) / 200;
        return baseHours * reliabilityFactor;
    }

    private static decimal CalculateConfidence(decimal merchantReliability, PaymentMethod method)
    {
        // Higher reliability = more predictable = higher confidence
        var baseConfidence = 50 + merchantReliability * 0.4m;

        // Mobile money is more predictable than bank transfers
        var methodBonus = method switch
        {
            PaymentMethod.EcoCash => 10,
            PaymentMethod.OneMoney => 8,
            PaymentMethod.InnBucks => 8,
            PaymentMethod.Telecash => 6,
            _ => 0
        };

        return Math.Clamp(baseConfidence + methodBonus, 0, 100);
    }

    private static List<string> BuildRiskFactors(Transaction tx, Merchant merchant)
    {
        var factors = new List<string>();

        if (merchant.ReliabilityScore < 60)
            factors.Add($"Low merchant reliability: {merchant.ReliabilityScore}%");

        if (tx.PaymentMethod is PaymentMethod.BankTransfer or PaymentMethod.ZimSwitch)
            factors.Add($"Slow payment method: {tx.PaymentMethod}");

        if (tx.PaidAt.HasValue)
        {
            var hours = (DateTime.UtcNow - tx.PaidAt.Value).TotalHours;
            var expected = (double)BaseSettlementHours.GetValueOrDefault(tx.PaymentMethod, 12m);
            if (hours > expected)
                factors.Add($"Overdue: {hours:F1}h elapsed vs {expected:F0}h expected");
        }

        var now = DateTime.UtcNow;
        if (now.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            factors.Add("Weekend processing delays");

        if (tx.Amount > 500)
            factors.Add($"High-value transaction: ${tx.Amount}");

        return factors;
    }
}

public record SettlementAnalysisInput(Guid MerchantId);
