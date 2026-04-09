namespace BMSBT.DTO;

/// <summary>
/// Input DTO for creating a MaintenanceBill record without touching existing MaintenanceNew UI.
/// </summary>
public class MaintenanceBillCreateDto
{
    // Core customer identity
    public string CustomerNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;

    // Location / plot info
    public string? BTNo { get; set; }
    public string? PlotStatus { get; set; }

    // Meter info (optional)
    public string? MeterNo { get; set; }

    // Tariff: MaintenanceTarrif row where Project, Category, Size match customer
    public string? Project { get; set; }
    public string? SubProject { get; set; }
    public string? PlotType { get; set; }
    public string? Size { get; set; }
    public string? Category { get; set; }

    // Billing period
    public string? BillingMonth { get; set; }
    public string? BillingYear { get; set; }

    // Billing-related dates (optional, can be populated from OperatorsSetup)
    public DateOnly? BillingDate { get; set; }
    public DateOnly? IssueDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public DateOnly? ValidDate { get; set; }
}

