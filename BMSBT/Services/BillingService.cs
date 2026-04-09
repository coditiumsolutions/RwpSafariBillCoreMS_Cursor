using System.Text.RegularExpressions;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BMSBT.Services;

public interface IBillingService
{
    TariffAmounts getTariff(string project, string category, string size);
    BillingCustomerResult generateMaintenanceBill(CustomersMaintenance customer, bool dryRun = false);
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

    public BillingCustomerResult generateMaintenanceBill(CustomersMaintenance customer, bool dryRun = false)
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

            var currentMonthYear = DateTime.Now.ToString("MM-yyyy");

            // Rule M-06: skip if GeneratedMonthYear already has current MM-YYYY.
            if (string.Equals(customer.GeneratedMonthYear?.Trim(), currentMonthYear, StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "Skipped";
                result.Reason = $"Already generated for {currentMonthYear}";
                _logger.LogInformation("Skipped customer {Uid}/{BTNo}: already generated for {MonthYear}.",
                    customer.Uid, customer.BTNo, currentMonthYear);
                return result;
            }

            var tariff = getTariff(customer.Project, customer.Category, customer.Size ?? string.Empty);
            result.BillAmount = tariff.BillAmount;
            result.TaxAmount = tariff.TaxAmount;

            // Rule M-02: TotalBill = BillAmount + TaxAmount
            var totalBill = Convert.ToInt32(Math.Round((double)(tariff.BillAmount + tariff.TaxAmount), MidpointRounding.AwayFromZero));
            result.TotalBill = totalBill;

            // Rule M-03: Surcharge = TotalBill * 10 / 100
            var surcharge = Convert.ToInt32(Math.Round(totalBill * 10.0 / 100.0, MidpointRounding.AwayFromZero));
            result.Surcharge = surcharge;

            // Rule M-04: BillInDueDate = TotalBill
            var billInDueDate = totalBill;
            result.BillInDueDate = billInDueDate;

            // Rule M-04: BillAfterDate = TotalBill + Surcharge
            var billAfterDate = Convert.ToInt32(Math.Round((double)(totalBill + surcharge), MidpointRounding.AwayFromZero));
            result.BillAfterDate = billAfterDate;

            var btNo = string.IsNullOrWhiteSpace(customer.BTNo) ? customer.CustomerNo : customer.BTNo;
            var now = DateTime.Now;
            var month = now.ToString("MM");
            var year = now.ToString("yyyy");

            // Step 2 logging requirement
            _logger.LogInformation(
                "Tariff found for customer {Uid}: Project={Project}, Category={Category}, Size={Size}, BillAmount={BillAmount}, TaxAmount={TaxAmount}, TotalBill={TotalBill}, Surcharge={Surcharge}, BillInDueDate={BillInDueDate}, BillAfterDate={BillAfterDate}",
                customer.Uid, customer.Project, customer.Category, customer.Size, tariff.BillAmount, tariff.TaxAmount, totalBill, surcharge, billInDueDate, billAfterDate);

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

                // Rounded integer values only.
                MaintCharges = tariff.BillAmount,
                TaxAmount = tariff.TaxAmount,
                BillAmountInDueDate = billInDueDate,
                BillSurcharge = surcharge,
                BillAmountAfterDueDate = billAfterDate,
                Arrears = 0,
                WaterCharges = 0,
                OtherCharges = 0,
                MiscCharges = 0,
                History = $"RulesVersion:{RulesVersionStamp}"
            };

            _dbContext.MaintenanceBills.Add(bill);
            customer.GeneratedMonthYear = currentMonthYear;
            _dbContext.CustomersMaintenance.Update(customer);
            _dbContext.SaveChanges();

            result.Status = "Generated";
            result.Reason = "Bill generated successfully";
            _logger.LogInformation("Generated bill for customer {Uid}/{BTNo}. Total={Total}, Surcharge={Surcharge}, AfterDue={AfterDue}.",
                customer.Uid, btNo, totalBill, surcharge, billAfterDate);

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
    public int TotalBill { get; set; }
    public int Surcharge { get; set; }
    public int BillInDueDate { get; set; }
    public int BillAfterDate { get; set; }
    public string Status { get; set; } = string.Empty; // Generated / Skipped / Failed / Preview
    public string Reason { get; set; } = string.Empty;
    public string RulesVersion { get; set; } = "1.3";
}

