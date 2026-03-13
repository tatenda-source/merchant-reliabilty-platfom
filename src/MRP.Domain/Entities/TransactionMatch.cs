namespace MRP.Domain.Entities;

public class TransactionMatch
{
    public Guid Id { get; set; }
    public Guid ReconciliationReportId { get; set; }
    public string Reference { get; set; } = string.Empty;
    public Guid? PaynowTransactionId { get; set; }
    public Guid? MerchantTransactionId { get; set; }
    public Guid? BankTransactionId { get; set; }
    public bool IsBalanced { get; set; }
    public string ResolutionStatus { get; set; } = "unresolved";

    public ReconciliationReport Report { get; set; } = null!;
    public Transaction? PaynowTransaction { get; set; }
    public Transaction? MerchantTransaction { get; set; }
    public Transaction? BankTransaction { get; set; }
    public ICollection<Anomaly> Anomalies { get; set; } = new List<Anomaly>();
}
