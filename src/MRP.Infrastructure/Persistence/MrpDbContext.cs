using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence;

public class MrpDbContext : DbContext
{
    public MrpDbContext(DbContextOptions<MrpDbContext> options) : base(options) { }

    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantIntegration> MerchantIntegrations => Set<MerchantIntegration>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<ReconciliationReport> ReconciliationReports => Set<ReconciliationReport>();
    public DbSet<RecoveryAttempt> RecoveryAttempts => Set<RecoveryAttempt>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<MerchantProfile> MerchantProfiles => Set<MerchantProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mrp");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MrpDbContext).Assembly);
    }
}
