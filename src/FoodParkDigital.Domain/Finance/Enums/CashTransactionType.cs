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
    Tip = 7,
    /// <summary>What a customer leaves in their favor (Fase 26D): it enters the drawer but is not a sale until they use it.</summary>
    CustomerDeposit = 8,
    /// <summary>What is given back to a customer of their balance in their favor (Fase 26D): it leaves the drawer.</summary>
    CustomerDepositReturn = 9
}
