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

    public MaintenanceBillInsertController(IMaintenanceBillInsertService service, BmsbtContext dbContext)
    {
        _service = service;
        _dbContext = dbContext;
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
    public async Task<IActionResult> CreateFromCustomerSelection([FromBody] int[] customerUids, CancellationToken cancellationToken)
    {
        try
        {
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
                    updates.Add(new { uid = customer.Uid, status = statusValue });
                    AddReason(statusValue);
                    continue;
                }

                // --- LotusScript: disconnected customer check ---
                if (string.Equals(customer.ConnectionStatus?.Trim(), "Disconnected", StringComparison.OrdinalIgnoreCase))
                {
                    statusValue = "Disconnected Customer";
                    customer.BillGenerationStatus = statusValue;
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
                        updates.Add(new { uid = customer.Uid, status = statusValue });
                        AddReason(statusValue);
                        continue;
                    }
                }

                if (shouldGenerate)
                {
                    // Check if Rate is defined (SubProject = Rates.Phase) before creating bill
                    var rate = LookupRateByPhase(customer.SubProject);
                    if (rate == null)
                    {
                        statusValue = "Rates Undefined";
                        customer.BillGenerationStatus = statusValue;
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

                    await _service.CreateAsync(dto, cancellationToken);

                    // Update BillGenerationStatus in CustomersMaintenance table
                    statusValue = $"{billingMonth}-{billingYear}";
                    customer.BillGenerationStatus = statusValue;
                    createdCount++;
                    AddReason("Created");
                }

                updates.Add(new { uid = customer.Uid, status = statusValue });
            }

            // Save all customer status updates to the database
            await _dbContext.SaveChangesAsync(cancellationToken);

            var matchedCount = customers.Count;
            var skippedCount = matchedCount - createdCount;

            return Ok(new
            {
                success = true,
                message = $"Maintenance bills process completed for {billingMonth} {billingYear}.",
                updates,
                summary = new
                {
                    selected = customerUids.Length,
                    matched = matchedCount,
                    created = createdCount,
                    skipped = skippedCount,
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

    private Rate? LookupRateByPhase(string? subProject)
    {
        var phase = subProject?.Trim() ?? "";
        if (string.IsNullOrEmpty(phase))
            return null;

        return _dbContext.Rates
            .AsEnumerable()
            .FirstOrDefault(r =>
                string.Equals(r.Phase?.Trim(), phase, StringComparison.OrdinalIgnoreCase));
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

