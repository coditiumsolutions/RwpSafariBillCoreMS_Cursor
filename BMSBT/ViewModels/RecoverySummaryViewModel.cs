namespace BMSBT.ViewModels;

public class RecoverySummaryViewModel
{
    public string Phase { get; set; } = string.Empty;

    public int TotalBills { get; set; }
    public int PaidBills { get; set; }
    public int UnpaidBills { get; set; }

    public decimal MaintenanceCharges { get; set; }
    public decimal MiscCharges { get; set; }
    public decimal MiscPaid { get; set; }
    public decimal WaterCharges { get; set; }
    public decimal RentCharges { get; set; }
}
