using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models;

public partial class MaintenanceBill
{
    [Key]
    public int Uid { get; set; }

    public string? InvoiceNo { get; set; }

    public string? CustomerNo { get; set; }

    public string? CustomerName { get; set; }

    public string? PlotStatus { get; set; }

    public string? MeterNo { get; set; }

    public string? Btno { get; set; }

    public string? BillingMonth { get; set; }

    public string? BillingYear { get; set; }

    public DateOnly? BillingDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateOnly? IssueDate { get; set; }

    public DateOnly? ValidDate { get; set; }

    public string? PaymentStatus { get; set; }

    public DateOnly? PaymentDate { get; set; }

    public string? PaymentMethod { get; set; }

    public string? BankDetail { get; set; }

    public DateTime? LastUpdated { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? MaintCharges { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? BillAmountInDueDate { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? BillSurcharge { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? BillAmountAfterDueDate { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? Arrears { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? TaxAmount { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? Fine { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? OtherCharges { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? WaterCharges { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? MiscCharges { get; set; }

    public string? History { get; set; }

    /// <summary>Matches DB: varchar 100.</summary>
    public string? FineDept { get; set; }
}
