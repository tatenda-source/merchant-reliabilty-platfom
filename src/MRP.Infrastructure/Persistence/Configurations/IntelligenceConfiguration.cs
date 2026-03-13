using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence.Configurations;

public class SettlementPredictionConfiguration : IEntityTypeConfiguration<SettlementPrediction>
{
    public void Configure(EntityTypeBuilder<SettlementPrediction> builder)
    {
        builder.ToTable("settlement_predictions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RiskScore).HasPrecision(5, 2);
        builder.Property(s => s.Confidence).HasPrecision(5, 2);
        builder.Property(s => s.PaymentMethod).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.RiskFactors).HasMaxLength(2048);

        builder.HasOne(s => s.Transaction)
            .WithMany()
            .HasForeignKey(s => s.TransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(s => s.Merchant)
            .WithMany()
            .HasForeignKey(s => s.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(s => new { s.MerchantId, s.CreatedAt });
        builder.HasIndex(s => s.RiskScore);
        builder.HasIndex(s => s.TransactionId);
    }
}

public class MerchantBehaviourProfileConfiguration : IEntityTypeConfiguration<MerchantBehaviourProfile>
{
    public void Configure(EntityTypeBuilder<MerchantBehaviourProfile> builder)
    {
        builder.ToTable("merchant_behaviour_profiles");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.AvgTransactionsPerMinute).HasPrecision(10, 4);
        builder.Property(m => m.AvgTransactionsPerHour).HasPrecision(10, 4);
        builder.Property(m => m.RetryRate).HasPrecision(5, 2);
        builder.Property(m => m.DuplicateRate).HasPrecision(5, 2);
        builder.Property(m => m.CallbackFailureRate).HasPrecision(5, 2);
        builder.Property(m => m.RiskScore).HasPrecision(5, 2);
        builder.Property(m => m.PeakTransactionsPerHour).HasPrecision(10, 4);
        builder.Property(m => m.ActiveAlerts).HasMaxLength(4096);

        builder.HasOne(m => m.Merchant)
            .WithMany()
            .HasForeignKey(m => m.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.MerchantId).IsUnique();
        builder.HasIndex(m => m.RiskScore);
    }
}

public class RecoveryStrategyDecisionConfiguration : IEntityTypeConfiguration<RecoveryStrategyDecision>
{
    public void Configure(EntityTypeBuilder<RecoveryStrategyDecision> builder)
    {
        builder.ToTable("recovery_strategy_decisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ChosenStrategy).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(r => r.DecisionReason).HasMaxLength(2048);
        builder.Property(r => r.MerchantReliabilityAtDecision).HasPrecision(5, 2);
        builder.Property(r => r.FinancialRiskAmount).HasPrecision(18, 4);

        builder.HasOne(r => r.Anomaly)
            .WithMany()
            .HasForeignKey(r => r.AnomalyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(r => r.Merchant)
            .WithMany()
            .HasForeignKey(r => r.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.AnomalyId);
        builder.HasIndex(r => new { r.ChosenStrategy, r.WasEffective });
    }
}
