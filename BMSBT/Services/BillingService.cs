using System.Globalization;
using System.Text.RegularExpressions;
using BMSBT.BillServices;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BMSBT.Services;

public interface IBillingService
{
    TariffAmounts getTariff(string project, string category, string size);
    BillingCustomerResult generateMaintenanceBill(
        CustomersMaintenance customer,
        bool dryRun = false,
        string? billingMonth = null,
        string? billingYear = null);
    MonthlyBillingSummary runMonthlyBillingForAll(bool dryRun = false);
}

public sealed class BillingService : IBillingService
{
    private readonly BmsbtContext _dbContext;
    private readonly ILogger<BillingService> _logger;
    private const string RulesVersionStamp = "1.3";

    public BillingService(BmsbtContext dbContext, ILogger<BillingService> logger, IHostEnvironment hostEnvironment)
    {
        _dbContext = dbContext;
        _logger = logger;

        // `.mdc` is documentation-only for developers/AI; runtime billing rules are C#.
        // We only compare versions to surface drift between documentation and code.
        var rulesFilePath = Path.Combine(hostEnvironment.ContentRootPath, ".cursor", "rules", "Billing_BusinessRules.mdc");
        var documentedVersion = ReadRulesVersion(rulesFilePath);
        if (!string.Equals(documentedVersion, "unknown", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(documentedVersion, RulesVersionStamp, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Billing rules version drift detected: code={CodeVersion}, documented={DocVersion}, file={RulesFile}. Runtime logic uses C# implementation.",
                RulesVersionStamp,
                documentedVersion,
                rulesFilePath);
        }
    }

    public string GetRulesVersionStamp() => RulesVersionStamp;

    public TariffAmounts getTariff(string project, string category, string size)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(size))
            {
                throw new InvalidOperationException("Tariff lookup failed: Project, Category, and Size are required.");
            }

            var rows = _dbContext.MaintenanceTarrifs
                .AsNoTracking()
                .Where(t =>
                    t.Project == project.Trim() &&
                    t.Category == category.Trim() &&
                    t.Size == size.Trim())
                .Select(t => new { t.Charges, t.Tax })
                .ToList();

            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Tariff not found for Project='{project}', Category='{category}', Size='{size}'.");
            }

            if (rows.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Duplicate tariff entries found for Project='{project}', Category='{category}', Size='{size}'.");
            }

            var tariffRow = rows[0];
            var billAmount = Convert.ToInt32(Math.Round(tariffRow.Charges, MidpointRounding.AwayFromZero));

            var taxRaw = (tariffRow.Tax ?? string.Empty).Trim().TrimEnd('%').Trim();
            if (!decimal.TryParse(taxRaw, out var parsedTax))
            {
                throw new InvalidOperationException(
                    $"Tariff lookup failed: invalid tax value '{tariffRow.Tax}' for Project='{project}', Category='{category}', Size='{size}'.");
            }

            var taxAmount = Convert.ToInt32(Math.Round(parsedTax, MidpointRounding.AwayFromZero));
            return new TariffAmounts(billAmount, taxAmount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "getTariff failed for Project={Project}, Category={Category}, Size={Size}", project, category, size);
            throw;
        }
    }

    public BillingCustomerResult generateMaintenanceBill(
        CustomersMaintenance customer,
        bool dryRun = false,
        string? billingMonth = null,
        string? billingYear = null)
    {
        var result = new BillingCustomerResult
        {
            CustomerUid = customer?.Uid ?? 0,
            CustomerNo = customer?.CustomerNo ?? string.Empty,
            BTNo = customer?.BTNo ?? string.Empty,
            CustomerName = customer?.CustomerName ?? string.Empty,
            Project = customer?.Project ?? string.Empty,
            Category = customer?.Category ?? string.Empty,
            Size = customer?.Size ?? string.Empty,
            PhaseName = customer?.SubProject ?? string.Empty,
            RulesVersion = RulesVersionStamp
        };

        try
        {
            if (customer == null)
            {
                throw new InvalidOperationException("Customer is required.");
            }

            var now = DateTime.Now;
            var targetMonth = string.IsNullOrWhiteSpace(billingMonth) ? now.ToString("MMMM") : billingMonth.Trim();
            var targetYear = string.IsNullOrWhiteSpace(billingYear) ? now.ToString("yyyy") : billingYear.Trim();
            var targetMonthYear = $"{targetMonth}-{targetYear}";
            var btNo = string.IsNullOrWhiteSpace(customer.BTNo) ? customer.CustomerNo : customer.BTNo;

            // Skip only when a bill already exists for the target billing period.
            var alreadyGeneratedForTargetPeriod = _dbContext.MaintenanceBills.Any(b =>
                b.Btno == btNo &&
                b.BillingMonth == targetMonth &&
                b.BillingYear == targetYear);
            if (alreadyGeneratedForTargetPeriod)
            {
                result.Status = "Skipped";
                result.Reason = $"Already generated for {targetMonthYear}";
                _logger.LogInformation("Skipped customer {Uid}/{BTNo}: already generated for {MonthYear}.",
                    customer.Uid, customer.BTNo, targetMonthYear);
                return result;
            }

            var maintCharges = Convert.ToInt32(Math.Round(customer.Maint ?? 0m, MidpointRounding.AwayFromZero));
            var miscCharges = Convert.ToInt32(Math.Round(customer.Misc ?? 0m, MidpointRounding.AwayFromZero));
            var customerWater = Convert.ToInt32(Math.Round(customer.Water ?? 0m, MidpointRounding.AwayFromZero));

            var btKey = (btNo ?? string.Empty).Trim();
            var additionalForBt = string.IsNullOrEmpty(btKey)
                ? new List<AdditionalCharge>()
                : _dbContext.AdditionalCharges.AsNoTracking()
                    .Where(a => a.BtNo != null && a.BtNo.Trim() == btKey)
                    .ToList();
            if (!additionalForBt.Any() && !string.IsNullOrEmpty(btKey))
            {
                additionalForBt = _dbContext.AdditionalCharges.AsNoTracking()
                    .Where(a => a.BtNo != null && a.BtNo == btNo)
                    .ToList();
            }

            var tableWaterDec = AdditionalChargeWaterBilling.SumWaterCharges(additionalForBt, targetMonth, targetYear);
            var waterCharges = AdditionalChargeWaterBilling.HasWaterChargeRows(additionalForBt)
                ? Convert.ToInt32(Math.Round(tableWaterDec, MidpointRounding.AwayFromZero))
                : customerWater;
            var rentCharges = Convert.ToInt32(Math.Round(customer.Rent ?? 0m, MidpointRounding.AwayFromZero));
            var generatorCharges = Convert.ToInt32(Math.Round(customer.Generator ?? 0m, MidpointRounding.AwayFromZero));
            var otherCharges = Convert.ToInt32(Math.Round(customer.Other ?? 0m, MidpointRounding.AwayFromZero));
            var foodSafetyCharges = Convert.ToInt32(Math.Round(customer.FoodSafety ?? 0m, MidpointRounding.AwayFromZero));
            var trollyTripCharges = Convert.ToInt32(Math.Round(customer.TrollyTrip ?? 0m, MidpointRounding.AwayFromZero));
            var extraWorkCharges = Convert.ToInt32(Math.Round(customer.ExtraWork ?? 0m, MidpointRounding.AwayFromZero));
            // "Other Charges" line on bill = Other + Generator (no separate Generator column in MaintenanceBills).
            var otherChargesLine = otherCharges + generatorCharges;

            // ServiceTaxGovt = Round( (maint * 40 / 100) * 16 / 100 )
            var serviceTaxGovt = Convert.ToInt32(Math.Round((double)maintCharges * 40.0 / 100.0 * 16.0 / 100.0, MidpointRounding.AwayFromZero));

            result.BillAmount = maintCharges;
            result.TaxAmount = serviceTaxGovt;

            // Current period subtotal (printed "CURRENT BILL" box, excludes arrears): all charge lines summed.
            var currentBill = maintCharges + miscCharges + serviceTaxGovt + waterCharges + otherChargesLine
                + rentCharges + foodSafetyCharges + trollyTripCharges + extraWorkCharges;
            result.TotalBill = currentBill;
            result.CurrentBillSubtotal = currentBill;

            var arrears = 0;
            var (previousMonth, previousYear) = GetPreviousMonthYear(targetMonth, targetYear);
            if (!string.IsNullOrWhiteSpace(previousMonth) && !string.IsNullOrWhiteSpace(previousYear))
            {
                var previousBill = _dbContext.MaintenanceBills
                    .Where(b => b.Btno == btNo && b.BillingMonth == previousMonth && b.BillingYear == previousYear)
                    .OrderByDescending(b => b.Uid)
                    .FirstOrDefault();

                if (previousBill != null &&
                    (string.IsNullOrWhiteSpace(previousBill.PaymentStatus) ||
                     previousBill.PaymentStatus.Equals("unpaid", StringComparison.OrdinalIgnoreCase)))
                {
                    arrears = previousBill.BillAmountAfterDueDate ?? 0;
                }
            }

            // Adjustments rule:
            // If BTNo has (AdjustmentName='excludearrears', AdjustmentValue=1) => arrears = 0
            // If BTNo has (AdjustmentName='excludesurcharge', AdjustmentValue=1) => surcharge = 0
            bool excludeArrears = false;
            bool excludeSurcharge = false;
            var adjustmentBtNo = customer.BTNo?.Trim();
            if (!string.IsNullOrWhiteSpace(adjustmentBtNo))
            {
                var adjustmentRows = _dbContext.Adjustments
                    .AsNoTracking()
                    .Where(a => a.BtNo != null && a.AdjustmentName != null && a.AdjustmentValue == 1 && a.BtNo == adjustmentBtNo)
                    .ToList();

                var matchingRows = adjustmentRows.Where(a =>
                    string.Equals(a.BtNo!.Trim(), adjustmentBtNo, StringComparison.OrdinalIgnoreCase));

                excludeArrears = matchingRows.Any(a =>
                    string.Equals(a.AdjustmentName!.Trim(), "excludearrears", StringComparison.OrdinalIgnoreCase));
                excludeSurcharge = matchingRows.Any(a =>
                    string.Equals(a.AdjustmentName!.Trim(), "excludesurcharge", StringComparison.OrdinalIgnoreCase));
            }

            if (excludeArrears)
            {
                arrears = 0;
            }

            // Surcharge = MaintCharges * 10 / 100 (no tax/misc included)
            var surcharge = Convert.ToInt32(Math.Round(maintCharges * 10.0 / 100.0, MidpointRounding.AwayFromZero));
            if (excludeSurcharge)
            {
                surcharge = 0;
            }
            result.Surcharge = surcharge;

            // BillInDueDate (Current Bill field) = current bill + unpaid previous arrears.
            var billInDueDate = currentBill + arrears;
            result.BillInDueDate = billInDueDate;

            // BillAfterDate = BillInDueDate + Surcharge
            var billAfterDate = billInDueDate + surcharge;
            result.BillAfterDate = billAfterDate;

            var month = targetMonth;
            var year = targetYear;

            // Step 2 logging requirement
            _logger.LogInformation(
                "Customer-maint rates used for {Uid}: Project={Project}, Category={Category}, Size={Size}, Maint={Maint}, Misc={Misc}, Water={Water}, Rent={Rent}, Generator={Generator}, Other={Other}, FoodSafety={FoodSafety}, TrollyTrip={TrollyTrip}, ExtraWork={ExtraWork}, ServiceTaxGovt={ServiceTaxGovt}, CurrentBillSubtotal={CurrentBillSubtotal}, Arrears={Arrears}, Surcharge={Surcharge}, BillInDueDate={BillInDueDate}, BillAfterDate={BillAfterDate}",
                customer.Uid, customer.Project, customer.Category, customer.Size, maintCharges, miscCharges, waterCharges, rentCharges, generatorCharges, otherCharges, foodSafetyCharges, trollyTripCharges, extraWorkCharges, serviceTaxGovt, currentBill, arrears, surcharge, billInDueDate, billAfterDate);

            if (dryRun)
            {
                result.Status = "Preview";
                result.Reason = "Dry-run: no DB write";
                return result;
            }

            var bill = new MaintenanceBill
            {
                Btno = btNo,
                CustomerName = customer.CustomerName,
                Project = customer.Project,
                Category = customer.Category,
                PhaseName = customer.SubProject,
                PlotStatus = customer.PlotStatus,
                BillingMonth = month,
                BillingYear = year,
                IssueDate = new DateOnly(now.Year, now.Month, 1),
                DueDate = new DateOnly(now.Year, now.Month, 15),
                PaymentStatus = "unpaid",

                MaintCharges = maintCharges,
                TaxAmount = serviceTaxGovt,
                BillAmountInDueDate = billInDueDate,
                BillSurcharge = surcharge,
                BillAmountAfterDueDate = billAfterDate,
                Arrears = arrears,
                WaterCharges = waterCharges,
                OtherCharges = otherChargesLine,
                MiscCharges = miscCharges,
                RentAmount = rentCharges,
                FoodSafety = foodSafetyCharges,
                TrollyTrip = trollyTripCharges,
                ExtraWork = extraWorkCharges,
                Compute = currentBill.ToString(CultureInfo.InvariantCulture),
                History = $"RulesVersion:{RulesVersionStamp}"
            };

            _dbContext.MaintenanceBills.Add(bill);
            customer.GeneratedMonthYear = targetMonthYear;
            _dbContext.CustomersMaintenance.Update(customer);
            _dbContext.SaveChanges();

            result.Status = "Generated";
            result.Reason = "Bill generated successfully";
            _logger.LogInformation("Generated bill for customer {Uid}/{BTNo}. CurrentBill={CurrentBill}, Arrears={Arrears}, Surcharge={Surcharge}, AfterDue={AfterDue}.",
                customer.Uid, btNo, currentBill, arrears, surcharge, billAfterDate);

            return result;
        }
        catch (Exception ex)
        {
            result.Status = "Failed";
            result.Reason = ex.Message;
            _logger.LogError(ex, "generateMaintenanceBill failed for customer {Uid}/{BTNo}.", customer?.Uid, customer?.BTNo);
            return result;
        }
    }

    public MonthlyBillingSummary runMonthlyBillingForAll(bool dryRun = false)
    {
        var summary = new MonthlyBillingSummary { RulesVersion = RulesVersionStamp };

        try
        {
            var customers = _dbContext.CustomersMaintenance
                .Where(c => c.ConnectionStatus != null && c.PlotStatus != null)
                .AsEnumerable()
                .Where(c =>
                    string.Equals(c.ConnectionStatus.Trim(), "Active", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.PlotStatus.Trim(), "Active", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var customer in customers)
            {
                try
                {
                    var result = generateMaintenanceBill(customer, dryRun);
                    summary.Logs.Add(result);
                    if (result.Status == "Generated")
                    {
                        summary.Generated++;
                    }
                    else if (result.Status == "Skipped" || result.Status == "Preview")
                    {
                        summary.Skipped++;
                    }
                    else
                    {
                        summary.Failed++;
                    }
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.LogError(ex, "Failed billing for customer {Uid}/{BTNo}. Continuing loop.", customer.Uid, customer.BTNo);
                }
            }

            _logger.LogInformation("Monthly billing summary: Generated={Generated}, Skipped={Skipped}, Failed={Failed}",
                summary.Generated, summary.Skipped, summary.Failed);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "runMonthlyBillingForAll failed.");
            throw;
        }
    }

    private static string ReadRulesVersion(string rulesFilePath)
    {
        try
        {
            if (!File.Exists(rulesFilePath))
            {
                return "unknown";
            }

            var content = File.ReadAllText(rulesFilePath);
            var match = Regex.Match(content, @"\*\*Version:\*\*\s*([0-9]+\.[0-9]+)");
            return match.Success ? match.Groups[1].Value.Trim() : "unknown";
        }
        catch
        {
            return "unknown";
        }
    }

    private static (string? month, string? year) GetPreviousMonthYear(string currentMonth, string currentYear)
    {
        var months = new[]
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        var monthIdx = Array.FindIndex(months, m => string.Equals(m, currentMonth, StringComparison.OrdinalIgnoreCase));
        if (monthIdx < 0 || !int.TryParse(currentYear, out var year))
        {
            return (null, null);
        }

        var previousMonthIdx = monthIdx == 0 ? 11 : monthIdx - 1;
        var previousYear = monthIdx == 0 ? year - 1 : year;
        return (months[previousMonthIdx], previousYear.ToString());
    }
}

public sealed record TariffAmounts(int BillAmount, int TaxAmount);

public sealed class MonthlyBillingSummary
{
    public string RulesVersion { get; set; } = "1.3";
    public int Generated { get; set; }
    public int Skipped { get; set; }
    public int Failed { get; set; }
    public List<BillingCustomerResult> Logs { get; set; } = new();
}

public sealed class BillingCustomerResult
{
    public int CustomerUid { get; set; }
    public string CustomerNo { get; set; } = string.Empty;
    public string BTNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string Project { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string PhaseName { get; set; } = string.Empty;
    public int BillAmount { get; set; }
    public int TaxAmount { get; set; }
    /// <summary>Same as <see cref="CurrentBillSubtotal"/>; kept for older preview scripts.</summary>
    public int TotalBill { get; set; }
    /// <summary>Sum of Maint + Sales Tax + Other + Water + Adv. Payment + Trolley + Food Safety + Misc + Extra Work (no arrears).</summary>
    public int CurrentBillSubtotal { get; set; }
    public int Surcharge { get; set; }
    public int BillInDueDate { get; set; }
    public int BillAfterDate { get; set; }
    public string Status { get; set; } = string.Empty; // Generated / Skipped / Failed / Preview
    public string Reason { get; set; } = string.Empty;
    public string RulesVersion { get; set; } = "1.3";
}

