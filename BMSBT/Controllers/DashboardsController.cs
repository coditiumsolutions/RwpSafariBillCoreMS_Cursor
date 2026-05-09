using BMSBT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BMSBT.Controllers
{
    public class DashboardsController : Controller
    {
        private readonly BmsbtContext _dbContext;

        public DashboardsController(BmsbtContext dbContext)
        {
            _dbContext = dbContext;
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                context.Result = RedirectToAction("Index", "Login");
                return;
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        private static List<string> GetMonthList()
        {
            return new List<string>
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
        }

        private static List<string> GetYearList()
        {
            return new List<string> { "2025", "2026", "2027" };
        }

        private List<string> GetProjectList()
        {
            return _dbContext.Configurations
                .AsNoTracking()
                .Where(c => c.ConfigKey != null
                            && c.ConfigValue != null
                            && c.ConfigKey.Trim().ToLower() == "projects")
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Maintenance bills whose linked customer row has ConnectionStatus &quot;Closed&quot; are excluded from dashboard totals.</summary>
        private static IQueryable<MaintenanceBill> ExcludeBillsWithClosedCustomer(
            IQueryable<MaintenanceBill> bills,
            IQueryable<CustomersMaintenance> customers)
        {
            return bills.Where(b =>
                !customers.Any(c =>
                    c.BTNo != null
                    && b.Btno != null
                    && c.BTNo.Trim() == b.Btno.Trim()
                    && c.ConnectionStatus != null
                    && c.ConnectionStatus.Trim().ToLower() == "closed"));
        }

        [HttpGet]
        public IActionResult Index(string? selectedYear, string? selectedMonth, string? apiProject, string? phaseNumber)
        {
            var year = string.IsNullOrWhiteSpace(selectedYear) ? DateTime.Now.Year.ToString() : selectedYear.Trim();
            var month = string.IsNullOrWhiteSpace(selectedMonth) ? DateTime.Now.ToString("MMMM") : selectedMonth.Trim();
            var project = string.IsNullOrWhiteSpace(apiProject) ? string.Empty : apiProject.Trim();
            var phase = string.IsNullOrWhiteSpace(phaseNumber) ? string.Empty : phaseNumber.Trim();

            ViewBag.SelectedYear = year;
            ViewBag.SelectedMonth = month;
            ViewBag.ApiProject = project;
            ViewBag.PhaseNumber = phase;

            var projects = GetProjectList();
            ViewBag.Projects = projects;
            ViewBag.Phases = !string.IsNullOrWhiteSpace(project)
                ? GetConfigurationCsvValuesByKey(project)
                : new List<string>();

            var customerCountsByProject = _dbContext.CustomersMaintenance
                .AsNoTracking()
                .GroupBy(c => c.Project)
                .Select(g => new
                {
                    Project = g.Key,
                    Count = g.Count()
                })
                .OrderBy(g => g.Project)
                .ToList();

            ViewBag.ProjectCustomerCounts = customerCountsByProject;
            ViewBag.TotalCustomerCount = _dbContext.CustomersMaintenance.Count();

            var local = SummarizeMaintenanceBillsFromLocalDb(month, year, project, phase);
            ViewBag.TotalBills = local.totalBills;
            ViewBag.TotalAmountGenerated = local.totalAmountGenerated;
            ViewBag.PaidCount = local.paidCount;
            ViewBag.PaidAmount = local.paidAmount;
            ViewBag.SurchargeCount = local.surchargeCount;
            ViewBag.SurchargeAmount = local.surchargeAmount;
            ViewBag.PartialCount = local.partialCount;
            ViewBag.PartialAmount = local.partialAmount;
            ViewBag.UnpaidBillsCount = local.unpaidCount;
            ViewBag.BillUnpaidAmount = local.unpaidAmount;
            ViewBag.BillsSummarySource = "local";

            // Reuse the existing Dashboard UI from Home module.
            return View("~/Views/Home/Dashboard.cshtml");
        }

        [HttpGet]
        public IActionResult Customers()
        {
            var customersByProject = _dbContext.CustomersMaintenance
                .AsNoTracking()
                .Where(c => c.Project != null && c.Project.Trim() != "")
                .GroupBy(c => c.Project!.Trim())
                .Select(g => new
                {
                    Project = g.Key,
                    Customers = g.Count()
                })
                .OrderBy(x => x.Project)
                .ToList();

            ViewBag.TotalCustomers = customersByProject.Sum(x => x.Customers);
            ViewBag.ChartLabelsJson = JsonSerializer.Serialize(customersByProject.Select(x => x.Project).ToList());
            ViewBag.ChartValuesJson = JsonSerializer.Serialize(customersByProject.Select(x => x.Customers).ToList());

            return View();
        }

        [HttpGet]
        public IActionResult ConnStatus()
        {
            var q = _dbContext.CustomersMaintenance.AsNoTracking();
            var total = q.Count();

            var connectedCount = q.Count(c =>
                c.ConnectionStatus != null
                && c.ConnectionStatus.Trim().ToLower() == "connected");

            var disconnectedCount = q.Count(c =>
                c.ConnectionStatus != null
                && c.ConnectionStatus.Trim().ToLower() == "disconnected");

            var closedCount = q.Count(c =>
                c.ConnectionStatus != null
                && c.ConnectionStatus.Trim().ToLower() == "closed");

            var summed = connectedCount + disconnectedCount + closedCount;
            var otherCount = summed > total ? 0 : total - summed;

            ViewBag.TotalCustomers = total;
            ViewBag.ConnectedCount = connectedCount;
            ViewBag.DisconnectedCount = disconnectedCount;
            ViewBag.ClosedCount = closedCount;
            ViewBag.OtherCount = otherCount;

            static double Pct(int part, int whole) =>
                whole <= 0 ? 0 : Math.Round(100.0 * part / whole, 1);

            ViewBag.PctConnected = Pct(connectedCount, total);
            ViewBag.PctDisconnected = Pct(disconnectedCount, total);
            ViewBag.PctClosed = Pct(closedCount, total);
            ViewBag.PctOther = Pct(otherCount, total);

            var labels = new List<string> { "Connected (Active)", "Disconnected", "Closed" };
            var values = new List<int> { connectedCount, disconnectedCount, closedCount };
            var colors = new List<string> { "#1cc88a", "#f6c23e", "#e74a3b" };

            if (otherCount > 0)
            {
                labels.Add("Other / not set");
                values.Add(otherCount);
                colors.Add("#858796");
            }

            ViewBag.ChartLabelsJson = JsonSerializer.Serialize(labels);
            ViewBag.ChartValuesJson = JsonSerializer.Serialize(values);
            ViewBag.ChartColorsJson = JsonSerializer.Serialize(colors);

            return View();
        }

        [HttpGet]
        public IActionResult Bills(string? billingMonth, string? billingYear, string? project)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(billingMonth) ? DateTime.Now.ToString("MMMM") : billingMonth.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(billingYear) ? DateTime.Now.Year.ToString() : billingYear.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();

            var billsQuery = _dbContext.MaintenanceBills
                .AsNoTracking()
                .Where(b => b.BillingMonth == selectedMonth && b.BillingYear == selectedYear);

            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                billsQuery = billsQuery.Where(b => b.Project != null && b.Project.Trim() == selectedProject);
            }

            billsQuery = ExcludeBillsWithClosedCustomer(billsQuery, _dbContext.CustomersMaintenance.AsNoTracking());

            var totalBills = billsQuery.Count();
            var generatedAmount = billsQuery.Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0m;
            var collectedAmount = billsQuery.Sum(b => (decimal?)b.AmountPaid) ?? 0m;
            var outstandingAmount = generatedAmount - collectedAmount;
            if (outstandingAmount < 0m) outstandingAmount = 0m;

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.TotalBills = totalBills;
            ViewBag.GeneratedAmount = generatedAmount;
            ViewBag.CollectedAmount = collectedAmount;
            ViewBag.OutstandingAmount = outstandingAmount;
            ViewBag.ChartLabelsJson = JsonSerializer.Serialize(new[] { "Generated", "Collected", "Outstanding" });
            ViewBag.ChartValuesJson = JsonSerializer.Serialize(new[] { generatedAmount, collectedAmount, outstandingAmount });

            return View();
        }

        [HttpGet]
        public IActionResult Collections(string? billingMonth, string? billingYear)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(billingMonth) ? DateTime.Now.ToString("MMMM") : billingMonth.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(billingYear) ? DateTime.Now.Year.ToString() : billingYear.Trim();

            var collectionsBillsQuery = _dbContext.MaintenanceBills
                .AsNoTracking()
                .Where(b => b.BillingMonth == selectedMonth && b.BillingYear == selectedYear)
                .Where(b => b.Project != null && b.Project.Trim() != "");

            collectionsBillsQuery = ExcludeBillsWithClosedCustomer(
                collectionsBillsQuery,
                _dbContext.CustomersMaintenance.AsNoTracking());

            var projectSummaries = collectionsBillsQuery
                .GroupBy(b => b.Project!.Trim())
                .Select(g => new
                {
                    Project = g.Key,
                    Generated = g.Sum(x => (decimal?)x.BillAmountInDueDate) ?? 0m,
                    Collected = g.Sum(x => (decimal?)x.AmountPaid) ?? 0m
                })
                .OrderBy(x => x.Project)
                .ToList()
                .Select(x => new
                {
                    x.Project,
                    x.Generated,
                    x.Collected,
                    Outstanding = Math.Max(0m, x.Generated - x.Collected)
                })
                .ToList();

            var allGenerated = projectSummaries.Sum(x => x.Generated);
            var allCollected = projectSummaries.Sum(x => x.Collected);
            var allOutstanding = Math.Max(0m, allGenerated - allCollected);

            var labels = projectSummaries.Select(x => x.Project).ToList();
            labels.Add("All Projects");

            var generatedValues = projectSummaries.Select(x => x.Generated).ToList();
            generatedValues.Add(allGenerated);

            var collectedValues = projectSummaries.Select(x => x.Collected).ToList();
            collectedValues.Add(allCollected);

            var outstandingValues = projectSummaries.Select(x => x.Outstanding).ToList();
            outstandingValues.Add(allOutstanding);

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.TotalGenerated = allGenerated;
            ViewBag.TotalCollected = allCollected;
            ViewBag.TotalOutstanding = allOutstanding;
            ViewBag.ChartLabelsJson = JsonSerializer.Serialize(labels);
            ViewBag.GeneratedJson = JsonSerializer.Serialize(generatedValues);
            ViewBag.CollectedJson = JsonSerializer.Serialize(collectedValues);
            ViewBag.OutstandingJson = JsonSerializer.Serialize(outstandingValues);

            return View();
        }

        [HttpGet]
        public IActionResult Users()
        {
            var users = _dbContext.Users
                .AsNoTracking()
                .ToList();

            var roleCounts = users
                .SelectMany(u =>
                {
                    var roleText = u.Role ?? string.Empty;
                    var roles = roleText
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(r => r.Trim())
                        .Where(r => !string.IsNullOrWhiteSpace(r))
                        .ToList();

                    if (!roles.Any())
                    {
                        roles.Add("No Role");
                    }

                    return roles;
                })
                .GroupBy(r => r, StringComparer.OrdinalIgnoreCase)
                .Select(g => new
                {
                    Role = g.Key,
                    Count = g.Count()
                })
                .OrderBy(x => x.Role)
                .ToList();

            ViewBag.TotalUsers = users.Count;
            ViewBag.ChartLabelsJson = JsonSerializer.Serialize(roleCounts.Select(x => x.Role).ToList());
            ViewBag.ChartValuesJson = JsonSerializer.Serialize(roleCounts.Select(x => x.Count).ToList());

            return View();
        }

        private List<string> GetConfigurationCsvValuesByKey(string configKey)
        {
            var rawValues = _dbContext.Configurations
                .AsNoTracking()
                .Where(c => c.ConfigKey != null && c.ConfigValue != null)
                .AsEnumerable()
                .Where(c => c.ConfigKey != null
                            && c.ConfigKey.Trim().Equals(configKey, StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .ToList();

            return rawValues
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ClassifyPaymentStatus(string? paymentStatus)
        {
            if (string.IsNullOrWhiteSpace(paymentStatus))
                return "unpaid";
            var t = paymentStatus.Trim().ToLowerInvariant();
            if (t == "paid")
                return "paid";
            if (t.Contains("surcharge", StringComparison.OrdinalIgnoreCase))
                return "surcharge";
            if (t.Contains("partial", StringComparison.OrdinalIgnoreCase) || t.Contains("parit", StringComparison.OrdinalIgnoreCase))
                return "partial";
            return "unpaid";
        }

        private (int totalBills, decimal totalAmountGenerated, int paidCount, decimal paidAmount, int surchargeCount, decimal surchargeAmount, int partialCount, decimal partialAmount, int unpaidCount, decimal unpaidAmount)
            SummarizeMaintenanceBillsFromLocalDb(string selectedMonth, string selectedYear, string? project, string? phase)
        {
            var selectedProject = (project ?? "").Trim();
            var selectedPhase = (phase ?? "").Trim();

            var customerBtNos = _dbContext.CustomersMaintenance
                .AsNoTracking()
                .Where(c => c.BTNo != null && c.BTNo.Trim() != "")
                .Where(c => string.IsNullOrWhiteSpace(selectedProject) || (c.Project != null && c.Project.Trim() == selectedProject))
                .Where(c => string.IsNullOrWhiteSpace(selectedPhase) || (c.SubProject != null && c.SubProject.Trim() == selectedPhase))
                .Select(c => c.BTNo!.Trim())
                .Distinct()
                .ToList();

            var billsQuery = _dbContext.MaintenanceBills
                .AsNoTracking()
                .Where(mb => mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear);

            if (customerBtNos.Any())
            {
                billsQuery = billsQuery.Where(mb => mb.Btno != null && customerBtNos.Contains(mb.Btno.Trim()));
            }

            var list = billsQuery
                .Select(mb => new
                {
                    Status = mb.PaymentStatus,
                    BillAmount = (decimal?)mb.BillAmountInDueDate ?? 0m,
                    AmountPaid = (decimal?)mb.AmountPaid ?? 0m
                })
                .ToList();

            int totalBills = list.Count;
            decimal totalAmountGenerated = list.Sum(x => x.BillAmount);
            int paidCount = 0, surchargeCount = 0, partialCount = 0, unpaidCount = 0;
            decimal paidAmount = 0m, surchargeAmount = 0m, partialAmount = 0m, unpaidAmount = 0m;

            foreach (var row in list)
            {
                var bucket = ClassifyPaymentStatus(row.Status);
                var effectivePaid = row.AmountPaid > 0m ? row.AmountPaid : 0m;
                var outstanding = row.BillAmount - effectivePaid;
                if (outstanding < 0m) outstanding = 0m;

                switch (bucket)
                {
                    case "paid":
                        paidCount++;
                        paidAmount += effectivePaid > 0m ? effectivePaid : row.BillAmount;
                        break;
                    case "surcharge":
                        surchargeCount++;
                        surchargeAmount += effectivePaid > 0m ? effectivePaid : row.BillAmount;
                        break;
                    case "partial":
                        partialCount++;
                        partialAmount += effectivePaid;
                        break;
                    default:
                        unpaidCount++;
                        unpaidAmount += outstanding > 0m ? outstanding : row.BillAmount;
                        break;
                }
            }

            return (totalBills, totalAmountGenerated, paidCount, paidAmount, surchargeCount, surchargeAmount, partialCount, partialAmount, unpaidCount, unpaidAmount);
        }
    }
}
