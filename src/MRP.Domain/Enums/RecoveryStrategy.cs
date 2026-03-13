namespace MRP.Domain.Enums;

public enum RecoveryStrategy
{
    AutoRetry = 0,
    MerchantNotification = 1,
    ManualEscalation = 2,
    PaynowDispute = 3
}
