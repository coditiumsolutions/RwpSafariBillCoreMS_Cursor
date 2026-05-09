namespace BMSBT.ViewModels;

public class OperationsUnpaidRowViewModel
{
    public string BtNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string BillingMonth { get; set; } = string.Empty;
    public string BillingYear { get; set; } = string.Empty;
    public DateOnly? DueDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public decimal BillAmount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal OutstandingAmount { get; set; }

    /// <summary>Older month, then newer month (calendar consecutive), both fully unpaid vs latest bill.</summary>
    public string ConsecutiveUnpaidMonths { get; set; } = "";
}
