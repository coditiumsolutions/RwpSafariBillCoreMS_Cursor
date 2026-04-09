using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
using BMSBT.Services;
using Microsoft.AspNetCore.Mvc;

namespace BMSBT.Controllers;

/// <summary>
/// Lightweight API controller for inserting MaintenanceBills.
/// This is intentionally isolated from existing MaintenanceNew UI and controllers.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class MaintenanceBillInsertController : ControllerBase
{
    private readonly IMaintenanceBillInsertService _service;
    private readonly BmsbtContext _dbContext;
    private readonly IBillingService _billingService;

    public MaintenanceBillInsertController(IMaintenanceBillInsertService service, BmsbtContext dbContext, IBillingService billingService)
    {
        _service = service;
        _dbContext = dbContext;
        _billingService = billingService;
    }

    /// <summary>
    /// Creates a new MaintenanceBills record using default business rules.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] MaintenanceBillCreateDto dto, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var result = await _service.CreateAsync(dto, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = result.Uid }, result);
    }

    /// <summary>
    /// Simple lookup endpoint mainly to satisfy CreatedAtAction.
    /// </summary>
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        // This endpoint is intentionally minimal and read-only,
        // to avoid interfering with existing MaintenanceNew flows.
        return NoContent();
    }

    /// <summary>
    /// Bulk create MaintenanceBills for a list of CustomersMaintenance UIDs.
    /// This is designed to be called from the MaintenanceNew/GenerateBill checkboxes.
    /// Uses BillingMonth/BillingYear from OperatorsSetup where OperatorName = 'Shahid'.
    /// </summary>
    [HttpPost("from-customers")]
    public async Task<IActionResult> CreateFromCustomerSelection([FromBody] BillingSelectionRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var customerUids = request?.SelectedIds ?? Array.Empty<int>();
            var dryRun = request?.DryRun ?? false;

            if (customerUids == null || customerUids.Length == 0)
            {
                return BadRequest(new { success = false, message = "No customers selected." });
            }

            // Pick BillingMonth, BillingYear and dates from OperatorsSetup for OperatorName = 'Shahid'
            var op = _dbContext.OperatorsSetups.FirstOrDefault(o => o.OperatorName == "Shahid");
            if (op == null)
            {
                return BadRequest(new { success = false, message = "Operator 'Shahid' not found in OperatorsSetup." });
            }

            if (string.IsNullOrEmpty(op.BillingMonth) || string.IsNullOrEmpty(op.BillingYear))
            {
                return BadRequest(new { success = false, message = "Please update OperatorsSetup for 'Shahid' with BillingMonth and BillingYear." });
            }

            string billingMonth = op.BillingMonth;
            string billingYear = op.BillingYear;
            DateOnly? billingDate = op.ReadingDate.HasValue ? DateOnly.FromDateTime(op.ReadingDate.Value) : (DateOnly?)null;
            DateOnly? issueDate = op.IssueDate.HasValue ? DateOnly.FromDateTime(op.IssueDate.Value) : (DateOnly?)null;
            DateOnly? dueDate = op.DueDate.HasValue ? DateOnly.FromDateTime(op.DueDate.Value) : (DateOnly?)null;
            DateOnly? validDate = op.ValidDate.HasValue ? DateOnly.FromDateTime(op.ValidDate.Value) : (DateOnly?)null;

            var customers = _dbContext.CustomersMaintenance
                .Where(c => customerUids.Contains(c.Uid))
                .ToList();

            if (!customers.Any())
            {
                return NotFound(new { success = false, message = "No matching customers found." });
            }

            var updates = new List<object>();
            var reasonCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var detailedLogs = new List<BillingCustomerResult>();
            int createdCount = 0;

            void AddReason(string reason)
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    reason = "Unknown";
                }

                if (reasonCounts.ContainsKey(reason))
                {
                    reasonCounts[reason]++;
                }
                else
                {
                    reasonCounts[reason] = 1;
                }
            }

            foreach (var customer in customers)
            {
                BillingCustomerResult BaseLog(string status, string reason) => new BillingCustomerResult
                {
                    CustomerUid = customer.Uid,
                    CustomerNo = customer.CustomerNo ?? string.Empty,
                    BTNo = customer.BTNo ?? string.Empty,
                    CustomerName = customer.CustomerName ?? string.Empty,
                    Project = customer.Project ?? string.Empty,
                    Category = customer.Category ?? string.Empty,
                    Size = customer.Size ?? string.Empty,
                    PhaseName = customer.SubProject ?? string.Empty,
                    Status = status,
                    Reason = reason,
                    RulesVersion = "1.3"
                };

                // dbo.CustomersMaintenance: BTNo only (legacy BTNoMaintenance column removed from schema).
                string? btNoForLookup = customer.BTNo?.Trim();
                string statusValue = "";
                bool shouldGenerate = false;

                // --- LotusScript: duplicate check (key = RefrenceNoBarCode + Billing_Year + Billing_Month) ---
                bool duplicateExists = !string.IsNullOrEmpty(btNoForLookup)
                    ? _dbContext.MaintenanceBills.Any(b =>
                          b.Btno == btNoForLookup &&
                          b.BillingMonth == billingMonth &&
                          b.BillingYear == billingYear)
                    : _dbContext.MaintenanceBills.Any(b =>
                          b.Btno == customer.CustomerNo &&
                          b.BillingMonth == billingMonth &&
                          b.BillingYear == billingYear);

                if (duplicateExists)
                {
                    statusValue = $"Bill Already Generated-{billingYear}-{billingMonth}";
                    customer.BillGenerationStatus = statusValue;
                    detailedLogs.Add(BaseLog("Skipped", statusValue));
                    updates.Add(new { uid = customer.Uid, status = statusValue });
                    AddReason(statusValue);
                    continue;
                }

                // --- LotusScript: disconnected customer check ---
                if (string.Equals(customer.ConnectionStatus?.Trim(), "Disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    statusValue = "Disconnected Customer";
                    customer.BillGenerationStatus = statusValue;
                    detailedLogs.Add(BaseLog("Skipped", statusValue));
                    updates.Add(new { uid = customer.Uid, status = statusValue });
                    AddReason(statusValue);
                    continue;
                }

                // --- LotusScript: previous bill logic (Billing_Month from operator = current month) ---
                // Previous month: January -> Dec prev year; February -> Jan same year; etc.
                var (previousMonth, previousYear) = GetPreviousMonthYear(billingMonth, billingYear);

                bool previousMonthBillExists = !string.IsNullOrEmpty(btNoForLookup)
                    ? _dbContext.MaintenanceBills.Any(b =>
                          b.Btno == btNoForLookup &&
                          b.BillingMonth == previousMonth &&
                          b.BillingYear == previousYear)
                    : _dbContext.MaintenanceBills.Any(b =>
                          b.Btno == customer.CustomerNo &&
                          b.BillingMonth == previousMonth &&
                          b.BillingYear == previousYear);

                if (previousMonthBillExists)
                {
                    // Previous month bill found -> generate current month
                    shouldGenerate = true;
                }
                else
                {
                    // Previous month bill NOT found -> check if customer has ANY bill (any month/year)
                    bool anyBillExists = !string.IsNullOrEmpty(btNoForLookup)
                        ? _dbContext.MaintenanceBills.Any(b => b.Btno == btNoForLookup)
                        : _dbContext.MaintenanceBills.Any(b => b.Btno == customer.CustomerNo);

                    if (!anyBillExists)
                    {
                        // No bills at all (e.g. no bill in last 12 months / new customer) -> generate
                        shouldGenerate = true;
                    }
                    else
                    {
                        // Last month bill missing but other bill(s) exist -> do not generate, set status
                        statusValue = "previous bill not exist";
                        customer.BillGenerationStatus = statusValue;
                        detailedLogs.Add(BaseLog("Skipped", statusValue));
                        updates.Add(new { uid = customer.Uid, status = statusValue });
                        AddReason(statusValue);
                        continue;
                    }
                }

                if (shouldGenerate)
                {
                    // MaintenanceTarrif: Project + Category + Size
                    var tariff = MaintenanceTariffLookup.FindTariff(_dbContext, customer.Project, customer.Category, customer.Size);
                    if (tariff == null)
                    {
                        statusValue = "Rates Undefined";
                        customer.BillGenerationStatus = statusValue;
                        detailedLogs.Add(BaseLog("Failed", statusValue));
                        updates.Add(new { uid = customer.Uid, status = statusValue });
                        AddReason(statusValue);
                        continue;
                    }

                    var dto = new MaintenanceBillCreateDto
                    {
                        CustomerNo = customer.CustomerNo ?? string.Empty,
                        CustomerName = customer.CustomerName ?? string.Empty,
                        BTNo = btNoForLookup,
                        PlotStatus = customer.PlotStatus,
                        MeterNo = null,

                        // Rate matching: SubProject (PhaseNumber) = Rates.Phase for MaintCharges
                        Project = customer.Project,
                        SubProject = customer.SubProject,
                        PlotType = customer.PlotStatus,
                        Size = customer.Size,
                        Category = customer.Category,

                        // Billing period and dates
                        BillingMonth = billingMonth,
                        BillingYear = billingYear,
                        BillingDate = billingDate,
                        IssueDate = issueDate,
                        DueDate = dueDate,
                        ValidDate = validDate
                    };

                    // Step 1-3: Billing rules service with optional dry-run
                    var result = _billingService.generateMaintenanceBill(customer, dryRun, billingMonth, billingYear);
                    detailedLogs.Add(result);
                    statusValue = result.Status;
                    if (!string.IsNullOrWhiteSpace(result.Reason))
                    {
                        statusValue = $"{result.Status}: {result.Reason}";
                    }

                    if (!dryRun && result.Status == "Generated")
                    {
                        createdCount++;
                        AddReason("Generated");
                    }
                    else if (result.Status == "Skipped" || result.Status == "Preview")
                    {
                        AddReason(result.Status);
                    }
                    else
                    {
                        AddReason("Failed");
                    }
                }
                else
                {
                    statusValue = "Not eligible for generation";
                    detailedLogs.Add(BaseLog("Skipped", statusValue));
                }

                updates.Add(new { uid = customer.Uid, status = statusValue });
            }

            // Save all customer status updates to the database
            if (!dryRun)
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            var matchedCount = customers.Count;
            var failedCount = detailedLogs.Count(x => x.Status == "Failed");
            var skippedCount = detailedLogs.Count(x => x.Status == "Skipped" || x.Status == "Preview");

            return Ok(new
            {
                success = true,
                message = dryRun
                    ? $"Dry-run completed for {billingMonth} {billingYear}. No bills were saved."
                    : $"Maintenance bills process completed for {billingMonth} {billingYear}.",
                dryRun,
                updates,
                customerLogs = detailedLogs,
                summary = new
                {
                    selected = customerUids.Length,
                    matched = matchedCount,
                    created = createdCount,
                    skipped = skippedCount,
                    failed = failedCount,
                    rulesVersion = "1.3",
                    reasons = reasonCounts
                }
            });
        }
        catch (Exception ex)
        {
            // Log the full exception details
            var message = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = $"Error generating MBills: {message}", details = ex.StackTrace });
        }
    }

    /// <summary>
    /// Previous month from operator billing month/year. January -> Dec prev year; February -> Jan same year; etc. (LotusScript logic)
    /// </summary>
    private (string month, string year) GetPreviousMonthYear(string currentMonth, string currentYear)
    {
        var months = new[] { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
        int monthIdx = Array.IndexOf(months, currentMonth);

        if (monthIdx == -1) return (currentMonth, currentYear);
        if (!int.TryParse(currentYear, out int year)) return (currentMonth, currentYear);

        int prevMonthIdx = monthIdx == 0 ? 11 : monthIdx - 1;
        int prevYear = monthIdx == 0 ? year - 1 : year;

        return (months[prevMonthIdx], prevYear.ToString());
    }
}

public sealed class BillingSelectionRequest
{
    public int[] SelectedIds { get; set; } = Array.Empty<int>();
    public bool DryRun { get; set; }
}

