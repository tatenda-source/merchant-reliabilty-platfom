using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence.Configurations;

public class ReconciliationReportConfiguration : IEntityTypeConfiguration<ReconciliationReport>
{
    public void Configure(EntityTypeBuilder<ReconciliationReport> builder)
    {
        builder.ToTable("reconciliation_reports");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TotalVolume).HasPrecision(18, 4);
        builder.Property(r => r.MatchedVolume).HasPrecision(18, 4);
        builder.Property(r => r.DiscrepancyVolume).HasPrecision(18, 4);

        builder.HasOne(r => r.Merchant)
            .WithMany(m => m.Reports)
            .HasForeignKey(r => r.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => new { r.MerchantId, r.GeneratedAt });
    }
}

public class AnomalyConfiguration : IEntityTypeConfiguration<Anomaly>
{
    public void Configure(EntityTypeBuilder<Anomaly> builder)
    {
        builder.ToTable("anomalies");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Severity).HasMaxLength(16);
        builder.Property(a => a.Description).HasMaxLength(1024);
        builder.Property(a => a.Amount).HasPrecision(18, 4);

        builder.HasOne(a => a.Merchant)
            .WithMany()
            .HasForeignKey(a => a.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(a => a.Transaction)
            .WithMany()
            .HasForeignKey(a => a.TransactionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(a => a.Report)
            .WithMany(r => r.Anomalies)
            .HasForeignKey(a => a.ReconciliationReportId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(a => new { a.IsResolved, a.Severity });
        builder.HasIndex(a => a.MerchantId);
    }
}

public class RecoveryAttemptConfiguration : IEntityTypeConfiguration<RecoveryAttempt>
{
    public void Configure(EntityTypeBuilder<RecoveryAttempt> builder)
    {
        builder.ToTable("recovery_attempts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Strategy).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.ConfidenceScore).HasPrecision(5, 2);
        builder.Property(r => r.DecisionReason).HasMaxLength(2048);

        builder.HasOne(r => r.Anomaly)
            .WithMany(a => a.RecoveryAttempts)
            .HasForeignKey(r => r.AnomalyId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => r.AnomalyId);
    }
}

public class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("settlements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Amount).HasPrecision(18, 4);
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
    }
}

public class MerchantProfileConfiguration : IEntityTypeConfiguration<MerchantProfile>
{
    public void Configure(EntityTypeBuilder<MerchantProfile> builder)
    {
        builder.ToTable("merchant_profiles");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.AvgTransactionsPerHour).HasPrecision(10, 4);
        builder.Property(m => m.PeakTransactionsPerHour).HasPrecision(10, 4);
        builder.Property(m => m.RetryRate).HasPrecision(5, 2);
        builder.Property(m => m.DuplicateRate).HasPrecision(5, 2);
        builder.Property(m => m.CallbackFailureRate).HasPrecision(5, 2);
        builder.Property(m => m.BehaviourRiskScore).HasPrecision(5, 2);
        builder.Property(m => m.ActiveAlerts).HasMaxLength(4096);

        builder.HasOne(m => m.Merchant)
            .WithMany()
            .HasForeignKey(m => m.MerchantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.MerchantId).IsUnique();
        builder.HasIndex(m => m.BehaviourRiskScore);
    }
}
