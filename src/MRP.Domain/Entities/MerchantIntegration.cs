namespace MRP.Domain.Entities;

public class MerchantIntegration
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public string PaynowIntegrationId { get; set; } = string.Empty;
    public string PaynowIntegrationKey { get; set; } = string.Empty;
    public string ResultUrl { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public bool IsCallbackReachable { get; set; }
    public DateTime? LastCallbackTestAt { get; set; }
    public DateTime? LastTransactionAt { get; set; }
    public int ConsecutiveFailures { get; set; }

    public Merchant Merchant { get; set; } = null!;
}
