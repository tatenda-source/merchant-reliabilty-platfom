namespace MRP.Domain.Enums;

public enum TransactionStatus
{
    Pending = 0,
    Paid = 1,
    AwaitingDelivery = 2,
    Delivered = 3,
    Failed = 4,
    Cancelled = 5,
    Refunded = 6,
    Disputed = 7
}
