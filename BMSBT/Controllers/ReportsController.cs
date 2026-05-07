using BMSBT.Models;
using BMSBT.Roles;
using BMSBT.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Data.SqlClient;

namespace BMSBT.Controllers
{
   
    public class ReportsController : Controller
    {
        private readonly BmsbtContext _dbContext;
        public ReportsController(BmsbtContext dbContext)
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

            if (!CurrentUserHasRole("Reports"))
            {
                TempData["AccessDeniedMessage"] = "you do no have rights to open the link";
                context.Result = RedirectToAction("AccessDenied", "Login");
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

        private List<string> GetPhaseList(string? selectedProject)
        {
            var query = _dbContext.CustomersMaintenance
                .AsNoTracking()
                .Where(c => c.SubProject != null && c.SubProject.Trim() != "");

            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                var p = selectedProject.Trim();
                query = query.Where(c => c.Project != null && c.Project.Trim() == p);
            }

            return query
                .Select(c => c.SubProject!)
                .AsEnumerable()
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
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

        [HttpGet]
        public IActionResult BillingSummary(string? month, string? year, string? project, string? phase)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();
            var selectedPhase = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase.Trim();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.Phases = GetPhaseList(selectedProject);
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhase = selectedPhase;

            var customersFiltered = _dbContext.CustomersMaintenance.AsQueryable();
            if (!string.IsNullOrWhiteSpace(selectedProject))
                customersFiltered = customersFiltered.Where(c => c.Project != null && c.Project.Trim() == selectedProject);
            if (!string.IsNullOrWhiteSpace(selectedPhase))
                customersFiltered = customersFiltered.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedPhase);

            var customerSummaryByProject = customersFiltered
                .GroupBy(c => c.Project)
                .Select(g => new { Project = g.Key ?? "", Customers = g.Count() })
                .OrderBy(x => x.Project)
                .ToList();

            var billsByProject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var billsQuery = from mb in _dbContext.MaintenanceBills
                             join cm in _dbContext.CustomersMaintenance on mb.Btno equals cm.BTNo
                             where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                             where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                             where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
                             group mb by cm.Project into g
                             select new { Project = g.Key ?? "", Count = g.Count() };
            foreach (var x in billsQuery)
                billsByProject[x.Project] = x.Count;

            var model = customerSummaryByProject
                .Select(c => new BillsSummaryCombinedViewModel
                {
                    Project = c.Project,
                    Customers = c.Customers,
                    BillsGenerated = billsByProject.TryGetValue(c.Project, out var bills) ? bills : 0
                })
                .ToList();

            return View(model);
        }

        [HttpGet]
        public IActionResult CustomersSummary(string? month, string? year, string? project, string? phase)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();
            var selectedPhase = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase.Trim();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.Phases = GetPhaseList(selectedProject);
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhase = selectedPhase;

            var customerQuery = _dbContext.CustomersMaintenance.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(selectedProject))
                customerQuery = customerQuery.Where(c => c.Project != null && c.Project.Trim() == selectedProject);
            if (!string.IsNullOrWhiteSpace(selectedPhase))
                customerQuery = customerQuery.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedPhase);

            var groupedCustomers = customerQuery
                .GroupBy(c => new { Project = c.Project ?? "", Phase = c.SubProject ?? "" })
                .Select(g => new { g.Key.Project, g.Key.Phase, Customers = g.Count() })
                .OrderBy(x => x.Project).ThenBy(x => x.Phase)
                .ToList();

            var groupedBills = (from mb in _dbContext.MaintenanceBills.AsNoTracking()
                                join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo
                                where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                                where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                                where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
                                group mb by new { Project = cm.Project ?? "", Phase = cm.SubProject ?? "" } into g
                                select new
                                {
                                    g.Key.Project,
                                    g.Key.Phase,
                                    Bills = g.Count(),
                                    BillAmount = g.Sum(x => (decimal?)x.BillAmountInDueDate) ?? 0m
                                })
                                .ToDictionary(
                                    x => $"{x.Project}|{x.Phase}",
                                    x => new { x.Bills, x.BillAmount },
                                    StringComparer.OrdinalIgnoreCase);

            var model = groupedCustomers
                .Select(x =>
                {
                    var key = $"{x.Project}|{x.Phase}";
                    groupedBills.TryGetValue(key, out var billsData);
                    return new CustomersSummaryRowViewModel
                    {
                        Project = x.Project,
                        Phase = x.Phase,
                        Customers = x.Customers,
                        BillsGenerated = billsData?.Bills ?? 0,
                        BillAmountGenerated = billsData?.BillAmount ?? 0m
                    };
                })
                .ToList();

            return View(model);
        }

        [HttpGet]
        public IActionResult Users()
        {
            var users = _dbContext.Users
                .AsNoTracking()
                .OrderBy(u => u.Username)
                .ToList();

            ViewBag.TotalUsers = users.Count;
            return View(users);
        }

        [HttpGet]
        public IActionResult Collections(string? month, string? year, string? project, string? phase)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();
            var selectedPhase = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase.Trim();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.Phases = GetPhaseList(selectedProject);
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhase = selectedPhase;

            var query = from mb in _dbContext.MaintenanceBills.AsNoTracking()
                        join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo
                        where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                        where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                        where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
                        select new
                        {
                            Status = mb.PaymentStatus,
                            AmountGenerated = (decimal?)mb.BillAmountInDueDate ?? 0m,
                            AmountPaid = (decimal?)mb.AmountPaid ?? 0m
                        };

            var model = query
                .AsEnumerable()
                .GroupBy(x => NormalizeCollectionStatus(x.Status))
                .Select(g => new CollectionsSummaryRowViewModel
                {
                    Status = g.Key,
                    TotalBillsGenerated = g.Count(),
                    TotalAmountGenerated = g.Sum(x => x.AmountGenerated),
                    TotalAmountCollected = g.Sum(x => x.AmountPaid)
                })
                .OrderBy(x => x.Status)
                .ToList();

            return View(model);
        }

        [HttpGet]
        public IActionResult Recovery(string? month, string? year, string? project, string? phase)
        {
            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();
            var selectedPhase = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase.Trim();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.Phases = GetPhaseList(selectedProject);
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhase = selectedPhase;

            var phaseList = string.IsNullOrWhiteSpace(selectedPhase)
                ? GetPhaseList(selectedProject)
                : new List<string> { selectedPhase };

            var billRows = (from mb in _dbContext.MaintenanceBills.AsNoTracking()
                            join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo
                            where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                            where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                            where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
                            select new
                            {
                                Phase = (cm.SubProject ?? "").Trim(),
                                mb.PaymentStatus,
                                mb.BillAmountInDueDate,
                                mb.AmountPaid,
                                mb.MaintCharges,
                                mb.MiscCharges,
                                mb.WaterCharges,
                                mb.RentAmount,
                                mb.ExtraWork,
                                mb.OtherCharges,
                                mb.Arrears
                            }).ToList();

            var groupedData = billRows
                .GroupBy(x => x.Phase)
                .Select(g => new RecoverySummaryViewModel
                {
                    Phase = g.Key,
                    TotalBills = g.Count(),
                    PaidBills = g.Count(b => b.PaymentStatus != null && b.PaymentStatus.Trim().ToLower() == "paid"),
                    UnpaidBills = g.Count(b => b.PaymentStatus == null || b.PaymentStatus.Trim().ToLower() != "paid"),
                    MaintenanceCharges = g.Sum(b => (decimal?)b.MaintCharges) ?? 0m,
                    MiscCharges = g.Sum(b => (decimal?)b.MiscCharges) ?? 0m,
                    MiscPaid = g.Sum(b =>
                    {
                        var misc = (decimal?)b.MiscCharges ?? 0m;
                        var totalBill = (decimal?)b.BillAmountInDueDate ?? 0m;
                        var amountPaid = (decimal?)b.AmountPaid ?? 0m;
                        if (misc <= 0m || amountPaid <= 0m || totalBill <= 0m)
                        {
                            return 0m;
                        }

                        var paidRatio = amountPaid / totalBill;
                        if (paidRatio > 1m) paidRatio = 1m;
                        if (paidRatio < 0m) paidRatio = 0m;
                        return misc * paidRatio;
                    }),
                    WaterCharges = g.Sum(b => (decimal?)b.WaterCharges) ?? 0m,
                    RentCharges = g.Sum(b => (decimal?)b.RentAmount) ?? 0m,
                    ExtraCharges = g.Sum(b => (decimal?)b.ExtraWork) ?? 0m,
                    OtherCharges = g.Sum(b => (decimal?)b.OtherCharges) ?? 0m,
                    Arrears = g.Sum(b => (decimal?)b.Arrears) ?? 0m,
                    PayableAmount = g.Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0m,
                    AmountPaid = g.Sum(b => (decimal?)b.AmountPaid) ?? 0m,
                    InstallmentPaid = g.Sum(b =>
                    {
                        var advance = (decimal?)b.RentAmount ?? 0m;
                        var totalBill = (decimal?)b.BillAmountInDueDate ?? 0m;
                        var amountPaid = (decimal?)b.AmountPaid ?? 0m;
                        if (advance <= 0m || amountPaid <= 0m || totalBill <= 0m)
                        {
                            return 0m;
                        }

                        var paidRatio = amountPaid / totalBill;
                        if (paidRatio > 1m) paidRatio = 1m;
                        if (paidRatio < 0m) paidRatio = 0m;
                        return advance * paidRatio;
                    }),
                    AdvanceAmount = g.Sum(b => (decimal?)b.RentAmount) ?? 0m
                }).ToList();

            var groupedLookup = groupedData.ToDictionary(x => x.Phase, StringComparer.OrdinalIgnoreCase);
            var model = phaseList
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .Select(phaseName =>
                {
                    if (groupedLookup.TryGetValue(phaseName, out var row))
                    {
                        return row;
                    }

                    return new RecoverySummaryViewModel
                    {
                        Phase = phaseName,
                        TotalBills = 0,
                        PaidBills = 0,
                        UnpaidBills = 0,
                        MaintenanceCharges = 0m,
                        MiscCharges = 0m,
                        MiscPaid = 0m,
                        WaterCharges = 0m,
                        RentCharges = 0m,
                        ExtraCharges = 0m,
                        OtherCharges = 0m,
                        Arrears = 0m,
                        PayableAmount = 0m,
                        AmountPaid = 0m,
                        InstallmentPaid = 0m,
                        AdvanceAmount = 0m
                    };
                })
                .ToList();

            return View(model);
        }

        [HttpGet]
        public IActionResult BillsHistory(string? logType, string? billingMonth, string? billingYear)
        {
            var selectedLogType = string.IsNullOrWhiteSpace(logType) ? string.Empty : logType.Trim();
            var selectedBillingMonth = string.IsNullOrWhiteSpace(billingMonth) ? string.Empty : billingMonth.Trim();
            var selectedBillingYear = string.IsNullOrWhiteSpace(billingYear) ? string.Empty : billingYear.Trim();
            var logTypes = GetConfigurationCsvValuesByKey("LogTypes");

            ViewBag.LogTypes = logTypes;
            ViewBag.SelectedLogType = selectedLogType;
            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.SelectedBillingMonth = selectedBillingMonth;
            ViewBag.SelectedBillingYear = selectedBillingYear;

            var query = _dbContext.AuditLogs
                .AsNoTracking()
                .OrderByDescending(a => a.ChangedAt)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(selectedLogType))
            {
                query = query.Where(a =>
                    (a.Operation != null && a.Operation.Trim() == selectedLogType) ||
                    (a.TableName != null && a.TableName.Trim() == selectedLogType) ||
                    (a.ModuleName != null && a.ModuleName.Trim() == selectedLogType));
            }

            if (!string.IsNullOrWhiteSpace(selectedBillingMonth))
            {
                if (DateTime.TryParseExact(selectedBillingMonth, "MMMM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var monthDate))
                {
                    var monthNumber = monthDate.Month;
                    query = query.Where(a => a.ChangedAt.Month == monthNumber);
                }
            }

            if (!string.IsNullOrWhiteSpace(selectedBillingYear) && int.TryParse(selectedBillingYear, out var yearNumber))
            {
                query = query.Where(a => a.ChangedAt.Year == yearNumber);
            }

            var model = query.Take(500).ToList();
            return View(model);
        }

        private static string NormalizeCollectionStatus(string? status)
        {
            var s = (status ?? "").Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(s))
                return "Unpaid";
            if (s == "paid")
                return "Paid";
            if (s == "paid with surcharge" || s == "paidwithsurcharge")
                return "Paid with surcharge";
            if (s == "partially paid" || s == "paritally paid")
                return "Partially Paid";
            if (s == "unpaid")
                return "Unpaid";
            return status!.Trim();
        }

        private bool CurrentUserHasRole(string roleName)
        {
            var rolesText = HttpContext.Session.GetString("Role");
            if (string.IsNullOrWhiteSpace(rolesText))
                return false;

            return rolesText
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Any(r => string.Equals(r.Trim(), roleName, StringComparison.OrdinalIgnoreCase));
        }

        public IActionResult Index(string? month, string? year, string? project)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();

            var monthList = GetMonthList();
            var yearList = GetYearList();
            var projects = GetProjectList();

            var customersQuery = _dbContext.CustomersMaintenance.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                customersQuery = customersQuery.Where(c => c.Project != null && c.Project.Trim() == selectedProject);
            }
            var totalCustomers = customersQuery.Count();

            var billsQuery = _dbContext.MaintenanceBills
                .AsNoTracking()
                .Where(b => b.BillingMonth == selectedMonth && b.BillingYear == selectedYear);

            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                billsQuery = billsQuery.Where(b => b.Project != null && b.Project.Trim() == selectedProject);
            }

            var totalBillsAmount = billsQuery.Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0m;
            var totalPaidAmount = billsQuery
                .Where(b => b.PaymentStatus != null && b.PaymentStatus.Trim().ToLower() == "paid")
                .Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0m;
            var totalUnpaidAmount = billsQuery
                .Where(b => b.PaymentStatus == null || b.PaymentStatus.Trim().ToLower() != "paid")
                .Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0m;

            ViewBag.Months = monthList;
            ViewBag.Years = yearList;
            ViewBag.Projects = projects;
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.TotalCustomers = totalCustomers;
            ViewBag.TotalBillsAmount = totalBillsAmount;
            ViewBag.TotalPaidAmount = totalPaidAmount;
            ViewBag.TotalUnpaidAmount = totalUnpaidAmount;

            return View();
        }

        [HttpGet]
        public IActionResult RecoveryReport(string? month, string? year, string? project)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var selectedMonth = string.IsNullOrWhiteSpace(month) ? DateTime.Now.ToString("MMMM") : month.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(year) ? DateTime.Now.Year.ToString() : year.Trim();
            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();

            var query = _dbContext.MaintenanceBills
                .AsNoTracking()
                .Where(b => b.BillingMonth == selectedMonth && b.BillingYear == selectedYear);

            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                query = query.Where(b => b.Project != null && b.Project.Trim() == selectedProject);
            }

            var rows = query
                .OrderBy(b => b.Btno)
                .ThenBy(b => b.CustomerName)
                .ToList();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.Projects = GetProjectList();
            ViewBag.SelectedMonth = selectedMonth;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedProject = selectedProject;

            return View(rows);
        }











        public IActionResult ExportToExcel()
        {
            // Fetch data from your DbContext
            var bills = _dbContext.SpecialDiscountBills.ToList();

            using (var package = new ExcelPackage())
            {
                // Add a worksheet to the package
                var worksheet = package.Workbook.Worksheets.Add("Special Discount Bills");

                // Add headers for all columns
                worksheet.Cells[1, 1].Value = "BT No";
                worksheet.Cells[1, 2].Value = "Customer Name";
                worksheet.Cells[1, 3].Value = "Project";
                worksheet.Cells[1, 4].Value = "Block";
                worksheet.Cells[1, 5].Value = "Sector";
                worksheet.Cells[1, 6].Value = "Plo No";
                worksheet.Cells[1, 7].Value = "Bill Amount";
                worksheet.Cells[1, 8].Value = "Invoice No";
                worksheet.Cells[1, 9].Value = "Billing Month";
                worksheet.Cells[1, 10].Value = "Billing Year";
                worksheet.Cells[1, 11].Value = "Billing Date";
                worksheet.Cells[1, 12].Value = "Due Date";
                worksheet.Cells[1, 13].Value = "Reading Date";
                worksheet.Cells[1, 14].Value = "Issue Date";
                worksheet.Cells[1, 15].Value = "Valid Date";
                worksheet.Cells[1, 16].Value = "Meter Type";
                worksheet.Cells[1, 17].Value = "Meter No";
                worksheet.Cells[1, 18].Value = "Payment Status";
                worksheet.Cells[1, 19].Value = "Total Unit";
                worksheet.Cells[1, 20].Value = "Amount Paid";
               
                worksheet.Cells[1, 21].Value = "Energy Cost";
                worksheet.Cells[1, 22].Value = "Last Updated";
                worksheet.Cells[1, 23].Value = "Bill Amount in Due Date";
                worksheet.Cells[1, 24].Value = "Bill Surcharge";
                worksheet.Cells[1, 25].Value = "Bill Amount After Due Date";
             

                // Add data rows
                int row = 2;
                foreach (var bill in bills)
                {
                    worksheet.Cells[1, 1].Value = "BT No";
                    worksheet.Cells[1, 2].Value = "Customer Name";
                    worksheet.Cells[1, 3].Value = "Project";
                    worksheet.Cells[1, 4].Value = "Block";
                    worksheet.Cells[1, 5].Value = "Sector";
                    worksheet.Cells[1, 6].Value = "Plo No";
                    worksheet.Cells[1, 7].Value = "Bill Amount";
                    worksheet.Cells[1, 8].Value = "Invoice No";
                    worksheet.Cells[1, 9].Value = "Billing Month";
                    worksheet.Cells[1, 10].Value = "Billing Year";
                    worksheet.Cells[1, 11].Value = "Billing Date";
                    worksheet.Cells[1, 12].Value = "Due Date";
                    worksheet.Cells[1, 13].Value = "Reading Date";
                    worksheet.Cells[1, 14].Value = "Issue Date";
                    worksheet.Cells[1, 15].Value = "Valid Date";
                    worksheet.Cells[1, 16].Value = "Meter Type";
                    worksheet.Cells[1, 17].Value = "Meter No";
                    worksheet.Cells[1, 18].Value = "Payment Status";
                    worksheet.Cells[1, 19].Value = "Total Unit";
                    worksheet.Cells[1, 20].Value = "Amount Paid";

                    worksheet.Cells[1, 21].Value = "Energy Cost";
                    worksheet.Cells[1, 22].Value = "Last Updated";
                    worksheet.Cells[1, 23].Value = "Bill Amount in Due Date";
                    worksheet.Cells[1, 24].Value = "Bill Surcharge";
                    worksheet.Cells[1, 25].Value = "Bill Amount After Due Date";

                    row++;
                }

                // Save the package as a byte array and return it as a file
                var fileContent = package.GetAsByteArray();
                return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "SpecialDiscountBills.xlsx");
            }
        }









        public async Task<IActionResult> GetBillsSpecialDiscount()
        {
            return View();   // Create the parameters using SqlParameter
        }

        [HttpPost]
        public async Task<IActionResult> GetBillsSpecialDiscount(string billingMonth, string billingYear, string project = null)
        {
            // Create the parameters using Microsoft.Data.SqlClient.SqlParameter
            var billingMonthParam = new Microsoft.Data.SqlClient.SqlParameter("@BillingMonth", billingMonth);
            var billingYearParam = new Microsoft.Data.SqlClient.SqlParameter("@BillingYear", billingYear);
            var projectParam = new Microsoft.Data.SqlClient.SqlParameter("@Project", project ?? (object)DBNull.Value);

            // Execute the stored procedure using FromSqlRaw with parameters
            var bills = await _dbContext.SpecialDiscountBills.FromSqlRaw(
                "EXEC dbo.usp_GetBillsSpecialDiscount @BillingMonth, @BillingYear, @Project",
                billingMonthParam, billingYearParam, projectParam)
                .ToListAsync();

            return View(bills); // Return the result to the view
        }




        public async Task<IActionResult> GetBillsRecoverable()
        {
            return View();   // Create the parameters using SqlParameter
        }

        [HttpPost]
        public async Task<IActionResult> GetBillsRecoverable(string billingMonth, string billingYear, string project = null)
        {
            // Create the parameters using Microsoft.Data.SqlClient.SqlParameter
            var billingMonthParam = new Microsoft.Data.SqlClient.SqlParameter("@BillingMonth", billingMonth);
            var billingYearParam = new Microsoft.Data.SqlClient.SqlParameter("@BillingYear", billingYear);
            var projectParam = new Microsoft.Data.SqlClient.SqlParameter("@Project", project ?? (object)DBNull.Value);

            // Execute the stored procedure using FromSqlRaw with parameters
            var bills = await _dbContext.SpecialDiscountBills.FromSqlRaw(
                "EXEC dbo.usp_GetBillsExcludingSpecialDiscount @BillingMonth, @BillingYear, @Project",
                billingMonthParam, billingYearParam, projectParam)
                .ToListAsync();

            return View(bills); // Return the result to the view
        }










        public async Task<IActionResult> GetTwoMonthOutstandingBills()
        {
            return View();
        }

        // POST: Execute the stored procedure and display results
        [HttpPost]
        public async Task<IActionResult> GetTwoMonthOutstandingBills(string billingMonth1, string billingMonth2, string billingYear, string project = null)
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrEmpty(billingMonth1) || string.IsNullOrEmpty(billingMonth2) || string.IsNullOrEmpty(billingYear))
                {
                    ModelState.AddModelError("", "Billing Month 1, Billing Month 2, and Billing Year are required.");
                    return View();
                }

                // Create the parameters using Microsoft.Data.SqlClient.SqlParameter
                var billingMonth1Param = new Microsoft.Data.SqlClient.SqlParameter("@BillingMonth1", billingMonth1);
                var billingMonth2Param = new Microsoft.Data.SqlClient.SqlParameter("@BillingMonth2", billingMonth2);
                var billingYearParam = new Microsoft.Data.SqlClient.SqlParameter("@BillingYear", billingYear);
                var projectParam = new Microsoft.Data.SqlClient.SqlParameter("@Project", project ?? (object)DBNull.Value);

                // Execute the stored procedure using FromSqlRaw with parameters
                var bills = await _dbContext.TwoMonthOutstandingBills.FromSqlRaw(
                    "EXEC dbo.usp_GetTwoMonthOutstandingBills @BillingMonth1, @BillingMonth2, @BillingYear, @Project",
                    billingMonth1Param, billingMonth2Param, billingYearParam, projectParam)
                    .ToListAsync();

                // Set ViewBag for search performed flag
                ViewBag.SearchPerformed = true;
                ViewBag.BillingMonth1 = billingMonth1;
                ViewBag.BillingMonth2 = billingMonth2;
                ViewBag.BillingYear = billingYear;
                ViewBag.Project = project;

                return View(bills); // Return the result to the view
            }
            catch (Exception ex)
            {
                // Log the exception (you should use your logging framework here)
                // _logger.LogError(ex, "Error executing GetTwoMonthOutstandingBills");

                ModelState.AddModelError("", "An error occurred while retrieving the bills. Please try again.");
                return View();
            }
        }

        // Optional: Export to Excel action
        [HttpPost]
        public async Task<IActionResult> ExportTwoMonthOutstandingBillsToExcel(string billingMonth1, string billingMonth2, string billingYear, string project = null)
        {
            try
            {
                // Validate required parameters
                if (string.IsNullOrEmpty(billingMonth1) || string.IsNullOrEmpty(billingMonth2) || string.IsNullOrEmpty(billingYear))
                {
                    return BadRequest("All required fields must be provided.");
                }

                // Use FormattableString to avoid SqlParameter issues
                var sql = $"EXEC dbo.usp_GetTwoMonthOutstandingBills {billingMonth1}, {billingMonth2}, {billingYear}, {project ?? "NULL"}";

                // Or use object array approach (safer)
                var bills = await _dbContext.TwoMonthOutstandingBills
                    .FromSqlRaw("EXEC dbo.usp_GetTwoMonthOutstandingBills {0}, {1}, {2}, {3}",
                        billingMonth1, billingMonth2, billingYear, project ?? (object)DBNull.Value)
                    .ToListAsync();

                if (!bills.Any())
                {
                    return BadRequest("No data found for the selected criteria.");
                }

                // Set EPPlus license context
                ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Outstanding Bills");

                // Style the header
                using (var range = worksheet.Cells[1, 1, 1, 10])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                    range.Style.Border.BorderAround(ExcelBorderStyle.Thick);
                }

                // Header
                var headers = new string[]
                {
            "BT No", "Customer Name", "Project", "Block", "Sector",
            "Plot No", "Months Outstanding", "Total Bill", "Total Paid", "Outstanding Amount"
                };

                for (int col = 0; col < headers.Length; col++)
                {
                    worksheet.Cells[1, col + 1].Value = headers[col];
                }

                // Data
                for (int i = 0; i < bills.Count; i++)
                {
                    var row = i + 2;
                    var bill = bills[i];

                    worksheet.Cells[row, 1].Value = bill.BTNo;
                    worksheet.Cells[row, 2].Value = bill.CustomerName;
                    worksheet.Cells[row, 3].Value = bill.Project;
                    worksheet.Cells[row, 4].Value = bill.Block;
                    worksheet.Cells[row, 5].Value = bill.Sector;
                    worksheet.Cells[row, 6].Value = bill.PloNo;
                    worksheet.Cells[row, 7].Value = bill.MonthsOutstanding;
                    worksheet.Cells[row, 8].Value = bill.TotalBill;
                    worksheet.Cells[row, 9].Value = bill.TotalPaid;
                    worksheet.Cells[row, 10].Value = bill.TotalOutstanding;
                }

                // Format currency columns
                worksheet.Cells[2, 8, bills.Count + 1, 10].Style.Numberformat.Format = "#,##0.00";

                // Auto-fit columns
                worksheet.Cells.AutoFitColumns();

                // Generate the Excel file
                var stream = new MemoryStream();
                await package.SaveAsAsync(stream);
                stream.Position = 0;

                string excelName = $"TwoMonthOutstandingBills_{DateTime.Now:yyyyMMddHHmmss}.xlsx";

                return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excelName);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error exporting to Excel: {ex.Message}");
            }
        }



    }
}
