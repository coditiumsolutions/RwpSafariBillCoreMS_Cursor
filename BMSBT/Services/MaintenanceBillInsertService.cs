using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;

namespace BMSBT.Services;

/// <summary>
/// Isolated service responsible only for inserting records into MaintenanceBills.
/// Does not depend on MaintenanceNew controllers or views.
/// </summary>
public interface IMaintenanceBillInsertService
{
    Task<MaintenanceBill> CreateAsync(MaintenanceBillCreateDto dto, CancellationToken cancellationToken = default);
}

public class MaintenanceBillInsertService : IMaintenanceBillInsertService
{
    private readonly BmsbtContext _dbContext;

    // Constants for billing calculations
    private const decimal SURCHARGE_PERCENTAGE = 0.10m; // 10% surcharge

    public MaintenanceBillInsertService(BmsbtContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<MaintenanceBill> CreateAsync(MaintenanceBillCreateDto dto, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var today = DateOnly.FromDateTime(now);

        // Match: MaintenanceTarrif on Project, Category, Size from customer / DTO.
        var tariff = MaintenanceTariffLookup.FindTariff(_dbContext, dto.Project, dto.Category, dto.Size);
        if (tariff == null)
        {
            throw new InvalidOperationException("Rates Undefined");
        }

        decimal maintCharges = (decimal)tariff.Charges;
        decimal taxAmount = MaintenanceTariffLookup.ParseTaxAmount(tariff.Tax);
        decimal miscCharges = 0m;

        // Carry forward arrears logic
        decimal arrears = 0;
        decimal fineToChargeSum = 0;
        decimal waterCharges = 0;
        decimal otherCharges = 0;

        if (!string.IsNullOrEmpty(dto.BillingMonth) && !string.IsNullOrEmpty(dto.BillingYear) && !string.IsNullOrEmpty(dto.BTNo))
        {
            var (prevMonth, prevYear) = GetPreviousMonthYear(dto.BillingMonth, dto.BillingYear);
            arrears = await GetArrearsAmountAsync(dto.BTNo, prevMonth, prevYear, cancellationToken);

            // Dynamic Fine logic: Sum FineToCharge from Fine table for matching BTNo, Month, and Year
            if (int.TryParse(dto.BillingYear, out int currentYearInt))
            {
                fineToChargeSum = await _dbContext.Fine
                    .Where(f => f.BTNo == dto.BTNo && 
                               f.FineMonth == dto.BillingMonth && 
                               f.FineYear == currentYearInt &&
                               f.FineService == "Maintenance")
                    .SumAsync(f => f.FineToCharge, cancellationToken);
            }

            // AdditionalCharges table currently only tracks:
            //   CustomerNo, ServiceName, ServiceType, Month, Year
            // and does not expose a numeric amount column. Until such a column
            // is added, we treat additional water/other charges as 0 here.
            waterCharges = 0m;
            otherCharges = 0m;
        }

        // Calculate billing amounts based on tariff values, arrears, fine, and additional charges (including MiscCharges from Rates)
        var billingCalculations = CalculateBillingAmounts(maintCharges, taxAmount, arrears, fineToChargeSum, waterCharges, otherCharges + miscCharges);

        var bill = new MaintenanceBill
        {
            // Customer mapping
            CustomerNo = dto.CustomerNo,
            CustomerName = dto.CustomerName,
            Btno = dto.BTNo,
            PlotStatus = dto.PlotStatus,
            MeterNo = dto.MeterNo,

            // Billing period (optional, can be null if caller doesn't provide)
            BillingMonth = dto.BillingMonth,
            BillingYear = dto.BillingYear,
            Project = dto.Project,
            Category = dto.Category,
            PhaseName = dto.SubProject,

            // Tariff-based values (DB columns are int)
            MaintCharges = (int)maintCharges,
            TaxAmount = (int)taxAmount,

            BillAmountInDueDate = (int)billingCalculations.BillAmountInDueDate,
            BillSurcharge = (int)billingCalculations.BillSurcharge,
            BillAmountAfterDueDate = (int)billingCalculations.BillAmountAfterDueDate,
            Arrears = (int)arrears,
            Fine = (int)fineToChargeSum,
            OtherCharges = (int)otherCharges,
            WaterCharges = (int)waterCharges,
            MiscCharges = (int)miscCharges,

            // Dates
            // Prefer values provided by caller (e.g., from OperatorsSetup), else fallback to today
            BillingDate = dto.BillingDate ?? today,
            IssueDate = dto.IssueDate ?? today,
            DueDate = dto.DueDate ?? today,
            ValidDate = dto.ValidDate ?? today,

        // Payment status fields (per requirement: all new bills start as unpaid)
        PaymentStatus = "unpaid",
        PaymentDate = null, // PaymentDate should be null for new bills
        PaymentMethod = "NA", // PaymentMethod should be "NA" (not "N/A")
        BankDetail = "NA", // BankDetail should be "NA" (not "N/A")
        
        LastUpdated = now,

            // Invoice number - simple unique placeholder logic
            InvoiceNo = GenerateInvoiceNo(now, dto.CustomerNo)
        };

        _dbContext.MaintenanceBills.Add(bill);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return bill;
    }

    /// <summary>
    /// Calculates billing amounts based on maintenance charges, tax amount, arrears, fine, and additional charges.
    /// Per Requirement:
    /// BillAmountInDueDate = Charges + Tax + Arrears + Fine + Water + Other
    /// BillSurcharge = (Charges + Tax) * 10 / 100
    /// BillAmountAfterDueDate = BillAmountInDueDate + BillSurcharge
    /// </summary>
    private static BillingCalculations CalculateBillingAmounts(decimal maintCharges, decimal taxAmount, decimal arrears = 0, decimal fine = 0, decimal water = 0, decimal other = 0)
    {
        // Step 1: Calculate BillAmountInDueDate = Charges + Tax + Arrears + Fine + Water + Other
        decimal inDueDateDecimal = maintCharges + taxAmount + arrears + fine + water + other;
        decimal billAmountInDueDate = Math.Round(inDueDateDecimal, MidpointRounding.AwayFromZero);

        // Step 2: Calculate Bill Surcharge = 10% of (Charges + Tax) -- Surcharge is usually on the base charges+tax
        decimal baseChargesAndTax = maintCharges + taxAmount;
        decimal surchargeDecimal = baseChargesAndTax * SURCHARGE_PERCENTAGE;
        decimal billSurcharge = Math.Round(surchargeDecimal, MidpointRounding.AwayFromZero);

        // Step 3: Calculate BillAmountAfterDueDate = BillAmountInDueDate + BillSurcharge
        decimal totalAfterDue = billAmountInDueDate + billSurcharge;
        decimal billAmountAfterDueDate = Math.Round(totalAfterDue, MidpointRounding.AwayFromZero);

        return new BillingCalculations
        {
            BillAmountInDueDate = billAmountInDueDate,
            BillSurcharge = billSurcharge,
            BillAmountAfterDueDate = billAmountAfterDueDate
        };
    }

    /// <summary>
    /// Fetches the arrears amount from the previous month's unpaid bill.
    /// </summary>
    private async Task<decimal> GetArrearsAmountAsync(string btNo, string prevMonth, string prevYear, CancellationToken cancellationToken)
    {
        var prevBill = await _dbContext.MaintenanceBills
            .Where(b => b.Btno == btNo && b.BillingMonth == prevMonth && b.BillingYear == prevYear)
            .OrderByDescending(b => b.Uid) // Get the latest bill for that month if multiple exist
            .FirstOrDefaultAsync(cancellationToken);

        if (prevBill != null && (string.IsNullOrEmpty(prevBill.PaymentStatus) || prevBill.PaymentStatus.Equals("unpaid", StringComparison.OrdinalIgnoreCase)))
        {
            return prevBill.BillAmountAfterDueDate ?? 0m;
        }

        return 0m;
    }

    /// <summary>
    /// Helper to calculate the previous month and year.
    /// </summary>
    private (string month, string year) GetPreviousMonthYear(string currentMonth, string currentYear)
    {
        var months = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        int monthIdx = Array.IndexOf(months, currentMonth);
        
        // If month not found in standard list, return current as fallback
        if (monthIdx == -1) return (currentMonth, currentYear);

        if (!int.TryParse(currentYear, out int year)) return (currentMonth, currentYear);

        int prevMonthIdx = monthIdx == 0 ? 11 : monthIdx - 1;
        int prevYear = monthIdx == 0 ? year - 1 : year;

        return (months[prevMonthIdx], prevYear.ToString());
    }

    /// <summary>
    /// Helper class to hold billing calculation results.
    /// </summary>
    private class BillingCalculations
    {
        public decimal BillAmountInDueDate { get; set; }
        public decimal BillSurcharge { get; set; }
        public decimal BillAmountAfterDueDate { get; set; }
    }

    /// <summary>
    /// Parses Tax string value to decimal.
    /// Handles percentage strings (e.g., "15%" -> 15) or numeric strings.
    /// Returns default value of 0 if parsing fails.
    /// </summary>
    private static decimal ParseTaxValue(string? taxString)
    {
        if (string.IsNullOrWhiteSpace(taxString))
        {
            return 0m; // Safe default
        }

        // Remove percentage sign if present
        var cleaned = taxString.Trim().TrimEnd('%').Trim();

        // Try parsing as decimal
        if (decimal.TryParse(cleaned, out decimal taxDecimal))
        {
            return taxDecimal;
        }

        // If all parsing fails, return safe default
        return 0m;
    }

    private static string GenerateInvoiceNo(DateTime now, string? customerNo)
    {
        // Per Requirement: YYYYMM + Last 5 digits of CUSTOMERNO
        // Example: 202601 + 22306 = 20260122306
        var datePart = now.ToString("yyyyMM");
        var cust = string.IsNullOrWhiteSpace(customerNo) ? "00000" : customerNo.Trim();
        
        // Get last 5 digits of customerNo
        var lastFive = cust.Length >= 5 ? cust[^5..] : cust.PadLeft(5, '0');
        
        return $"{datePart}{lastFive}";
    }
}


