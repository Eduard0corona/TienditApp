namespace FoodParkDigital.Domain.Finance.Enums;

public enum CashTransactionType
{
    SalePayment = 0,
    Refund = 1,
    Expense = 2,
    Withdrawal = 3,
    Deposit = 4,
    OpeningAdjustment = 5,
    ClosingAdjustment = 6,
    /// <summary>A tip given with a sale payment: it enters the drawer but is not a sale.</summary>
    Tip = 7
}
