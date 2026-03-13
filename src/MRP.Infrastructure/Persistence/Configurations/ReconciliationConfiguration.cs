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

public class TransactionMatchConfiguration : IEntityTypeConfiguration<TransactionMatch>
{
    public void Configure(EntityTypeBuilder<TransactionMatch> builder)
    {
        builder.ToTable("transaction_matches");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Reference).HasMaxLength(128);
        builder.Property(m => m.ResolutionStatus).HasMaxLength(32);

        builder.HasOne(m => m.Report)
            .WithMany(r => r.Matches)
            .HasForeignKey(m => m.ReconciliationReportId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(m => m.Reference);
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

        builder.HasOne(a => a.Match)
            .WithMany(m => m.Anomalies)
            .HasForeignKey(a => a.TransactionMatchId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.IsResolved, a.Severity });
    }
}

public class RecoveryAttemptConfiguration : IEntityTypeConfiguration<RecoveryAttempt>
{
    public void Configure(EntityTypeBuilder<RecoveryAttempt> builder)
    {
        builder.ToTable("recovery_attempts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Strategy).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(r => r.Anomaly)
            .WithOne(a => a.RecoveryAttempt)
            .HasForeignKey<RecoveryAttempt>(r => r.AnomalyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AgentTaskConfiguration : IEntityTypeConfiguration<AgentTask>
{
    public void Configure(EntityTypeBuilder<AgentTask> builder)
    {
        builder.ToTable("agent_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AgentType).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.TaskType).HasMaxLength(64);
        builder.Property(t => t.Status).HasMaxLength(16);

        builder.HasOne(t => t.Result)
            .WithOne(r => r.Task)
            .HasForeignKey<AgentResult>(r => r.AgentTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => new { t.AgentType, t.Status, t.Priority });
    }
}

public class AgentResultConfiguration : IEntityTypeConfiguration<AgentResult>
{
    public void Configure(EntityTypeBuilder<AgentResult> builder)
    {
        builder.ToTable("agent_results");
        builder.HasKey(r => r.Id);
    }
}
