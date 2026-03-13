using MRP.Domain.Enums;

namespace MRP.Domain.Entities;

public class Merchant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string TradingName { get; set; } = string.Empty;
    public string ContactEmail { get; set; } = string.Empty;
    public string ContactPhone { get; set; } = string.Empty;
    public MerchantTier Tier { get; set; } = MerchantTier.Standard;
    public bool IsActive { get; set; }
    public DateTime OnboardedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public decimal ReliabilityScore { get; set; }

    public MerchantIntegration? Integration { get; set; }
    public ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public ICollection<ReconciliationReport> Reports { get; set; } = new List<ReconciliationReport>();
}
