using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models;

public partial class MaintenanceBill
{
    [Key]
    [Column("uid")]
    public int Uid { get; set; }

    /// <summary>Not persisted; MaintenanceBills has BTNo only (see db.txt).</summary>
    [NotMapped]
    public string? InvoiceNo { get; set; }

    /// <summary>Alias for <see cref="Btno"/> (BTNo column). Not a separate SQL column.</summary>
    [NotMapped]
    public string? CustomerNo
    {
        get => Btno;
        set => Btno = value;
    }

    public string? CustomerName { get; set; }

    public string? PlotStatus { get; set; }

    [Column("Project")]
    public string? Project { get; set; }

    [Column("Category")]
    public string? Category { get; set; }

    [NotMapped]
    public string? MeterNo { get; set; }

    [Column("BTNo")]
    public string? Btno { get; set; }

    [Column("PhaseName")]
    public string? PhaseName { get; set; }

    public string? BillingMonth { get; set; }

    public string? BillingYear { get; set; }

    [NotMapped]
    public DateOnly? BillingDate { get; set; }

    public DateOnly? DueDate { get; set; }

    public DateOnly? IssueDate { get; set; }

    [NotMapped]
    public DateOnly? ValidDate { get; set; }

    public string? PaymentStatus { get; set; }

    public DateOnly? PaymentDate { get; set; }

    public string? PaymentMethod { get; set; }

    public string? BankDetail { get; set; }

    [NotMapped]
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

    /// <summary>DB column: current_gst.</summary>
    [Column("current_gst")]
    public int? TaxAmount { get; set; }

    [NotMapped]
    public int? Fine { get; set; }

    /// <summary>Matches DB: int. Typically Other + Generator from CustomersMaintenance.</summary>
    public int? OtherCharges { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? WaterCharges { get; set; }

    /// <summary>Matches DB: int.</summary>
    public int? MiscCharges { get; set; }

    /// <summary>Matches DB RentAmount (Adv. Payment on printed bill).</summary>
    public int? RentAmount { get; set; }

    /// <summary>Matches DB FoodSafety.</summary>
    public int? FoodSafety { get; set; }

    /// <summary>Matches DB TrollyTrip.</summary>
    public int? TrollyTrip { get; set; }

    /// <summary>Matches DB ExtraWork.</summary>
    public int? ExtraWork { get; set; }

    public string? History { get; set; }

    /// <summary>DB column <c>compute</c>: stores current-period subtotal (Maint + Tax + Other + Water + Adv + Trolley + Food Safety + Misc + Extra Work), no arrears.</summary>
    [Column("compute")]
    public string? Compute { get; set; }

    [NotMapped]
    public string? FineDept { get; set; }
}

