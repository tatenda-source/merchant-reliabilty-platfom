using Microsoft.EntityFrameworkCore;
using MRP.Domain.Entities;

namespace MRP.Infrastructure.Persistence;

public class MrpDbContext : DbContext
{
    public MrpDbContext(DbContextOptions<MrpDbContext> options) : base(options) { }

    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<MerchantIntegration> MerchantIntegrations => Set<MerchantIntegration>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<TransactionMatch> TransactionMatches => Set<TransactionMatch>();
    public DbSet<Anomaly> Anomalies => Set<Anomaly>();
    public DbSet<ReconciliationReport> ReconciliationReports => Set<ReconciliationReport>();
    public DbSet<RecoveryAttempt> RecoveryAttempts => Set<RecoveryAttempt>();
    public DbSet<AgentTask> AgentTasks => Set<AgentTask>();
    public DbSet<AgentResult> AgentResults => Set<AgentResult>();
    public DbSet<SettlementPrediction> SettlementPredictions => Set<SettlementPrediction>();
    public DbSet<MerchantBehaviourProfile> MerchantBehaviourProfiles => Set<MerchantBehaviourProfile>();
    public DbSet<RecoveryStrategyDecision> RecoveryStrategyDecisions => Set<RecoveryStrategyDecision>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("mrp");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MrpDbContext).Assembly);
    }
}
