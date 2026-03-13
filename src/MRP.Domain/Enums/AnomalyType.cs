namespace MRP.Domain.Enums;

public enum AnomalyType
{
    MissingPaynowRecord = 0,
    MissingMerchantRecord = 1,
    MissingBankRecord = 2,
    StatusMismatch = 3,
    AmountDiscrepancy = 4,
    DuplicateTransaction = 5,
    SettlementDelay = 6,
    VelocityAnomaly = 7,
    CallbackFailure = 8
}
