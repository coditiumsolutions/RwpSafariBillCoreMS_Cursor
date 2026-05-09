using System.Globalization;
using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
using BMSBT.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using X.PagedList;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    public class OperationsController : Controller
    {
        private readonly BmsbtContext _dbContext;
        private readonly ICurrentOperatorService _operatorService;

        public OperationsController(BmsbtContext dbContext, ICurrentOperatorService operatorService)
        {
            _dbContext = dbContext;
            _operatorService = operatorService;
        }

        public async Task<IActionResult> Index()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            var operatorId = ResolveOperatorIdForBilling();
            if (string.IsNullOrEmpty(operatorId))
            {
                TempData["ErrorMessage"] = "Operator ID not found in session. Please log out and log in again.";
                return RedirectToAction("Index", "Login");
            }

            try
            {
                await _operatorService.InitializeAsync(operatorId);
            }
            catch (KeyNotFoundException)
            {
                TempData["ErrorMessage"] = $"No operator setup found for ID '{operatorId}'. Check Operators Setup.";
                return RedirectToAction("Index", "Home");
            }

            var currentOperator = _operatorService.GetCurrentOperator();
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            ViewBag.BankBranches = LoadBankBranchesForPaymentForm();

            var model = new BillViewModel
            {
                BillingMonth = currentOperator.BillingMonth,
                BillingYear = currentOperator.BillingYear,
                PaymentType = "Paid",
                PaidOn = DateTime.Today
            };

            return View("~/Views/MaintenanceBill/PaymentForm.cshtml", model);
        }

        private string? ResolveOperatorIdForBilling()
        {
            var op = HttpContext.Session.GetString("OperatorId");
            if (!string.IsNullOrWhiteSpace(op))
                return op.Trim();

            var detailJson = HttpContext.Session.GetString("OperatorSetupDetail");
            if (!string.IsNullOrEmpty(detailJson))
            {
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(detailJson);
                    if (doc.RootElement.TryGetProperty("OperatorId", out var el))
                    {
                        var s = el.GetString();
                        if (!string.IsNullOrWhiteSpace(s))
                            return s.Trim();
                    }
                }
                catch
                {
                    // ignore malformed session JSON
                }
            }

            var userName = HttpContext.Session.GetString("UserName");
            if (!string.IsNullOrEmpty(userName))
            {
                var setup = _dbContext.OperatorsSetups
                    .FirstOrDefault(o => o.OperatorName == userName);
                if (!string.IsNullOrWhiteSpace(setup?.OperatorID))
                    return setup.OperatorID!.Trim();
            }

            return null;
        }

        private List<string> LoadBankBranchesForPaymentForm()
        {
            const string banksKey = "banks";
            var raw = _dbContext.Configurations
                .AsNoTracking()
                .Where(c => c.ConfigKey != null &&
                            c.ConfigKey.Trim().ToLower() == banksKey &&
                            !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .FirstOrDefault();

            return ParseCommaSeparatedConfigValues(raw);
        }

        private static List<string> ParseCommaSeparatedConfigValues(string? configValue)
        {
            if (string.IsNullOrWhiteSpace(configValue))
                return new List<string>();

            return configValue
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        [HttpGet]
        public IActionResult Disconnections(string? project, string? phase, string? show, int? page)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            const int pageSize = 10;
            var pageNumber = Math.Max(page.GetValueOrDefault(1), 1);

            var selectedProject = string.IsNullOrWhiteSpace(project) ? string.Empty : project.Trim();
            var selectedPhase = string.IsNullOrWhiteSpace(phase) ? string.Empty : phase.Trim();

            var showClicked = string.Equals(show, "1", StringComparison.OrdinalIgnoreCase);
            var shouldRunQuery = showClicked && !string.IsNullOrWhiteSpace(selectedProject);

            if (showClicked && string.IsNullOrWhiteSpace(selectedProject))
            {
                ViewBag.FilterMessage = "Please select a project.";
            }

            ViewBag.DataLoaded = shouldRunQuery;

            var disconnected = new List<OperationsUnpaidRowViewModel>();
            if (shouldRunQuery)
            {
                var rawRows = (from mb in _dbContext.MaintenanceBills.AsNoTracking()
                        join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo into cmGroup
                        from cm in cmGroup.DefaultIfEmpty()
                        select new { mb, cm })
                    .AsEnumerable()
                    .Where(x => !string.IsNullOrWhiteSpace(x.mb.Btno))
                    .Select(x =>
                    {
                        var projectName = x.cm != null ? (x.cm.Project ?? "") : (x.mb.Project ?? "");
                        var phaseName = x.cm != null ? (x.cm.SubProject ?? "") : (x.mb.PhaseName ?? "");
                        return new
                        {
                            Bill = x.mb,
                            Project = projectName.Trim(),
                            Phase = phaseName.Trim()
                        };
                    })
                    .Where(x => string.IsNullOrWhiteSpace(selectedProject)
                                || string.Equals(x.Project, selectedProject, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.IsNullOrWhiteSpace(selectedPhase)
                                || string.Equals(x.Phase, selectedPhase, StringComparison.OrdinalIgnoreCase));

                var byBtNo = rawRows.GroupBy(x => x.Bill.Btno!.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var grp in byBtNo)
                {
                    var tuples = grp.Select(x => (x.Bill, x.Project, x.Phase)).ToList();
                    if (TryBuildTwoConsecutiveUnpaidMonthsRow(tuples, out var row))
                    {
                        disconnected.Add(row);
                    }
                }

                disconnected = disconnected.OrderBy(x => x.Project).ThenBy(x => x.Phase).ThenBy(x => x.BtNo).ToList();
            }

            var paged = shouldRunQuery
                ? disconnected.ToPagedList(pageNumber, pageSize)
                : new List<OperationsUnpaidRowViewModel>().ToPagedList(1, pageSize);

            ViewBag.Projects = GetProjectListFromConfiguration();
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhase = selectedPhase;

            return View(paged);
        }

        /// <summary>
        /// Uses the customer's latest generated billing period (max month/year among bills), then requires the
        /// immediately preceding calendar month to also have bills. Both periods must be fully unpaid — e.g.
        /// when May is latest, April+May both unpaid; once June generates, May+June both unpaid are checked.
        /// </summary>
        private static bool TryBuildTwoConsecutiveUnpaidMonthsRow(
            List<(MaintenanceBill Bill, string Project, string Phase)> items,
            out OperationsUnpaidRowViewModel row)
        {
            row = null!;
            var enriched = new List<(int yOrd, int mOrd, MaintenanceBill Bill, string Project, string Phase)>();
            foreach (var x in items)
            {
                if (!TryParseBillingPeriodOrdinal(x.Bill.BillingMonth, x.Bill.BillingYear, out var yOrd, out var mOrd))
                    continue;
                enriched.Add((yOrd, mOrd, x.Bill, x.Project, x.Phase));
            }

            if (enriched.Count == 0)
                return false;

            var byPeriod = enriched.GroupBy(e => (e.yOrd, e.mOrd)).ToList();
            var latestPeriod = byPeriod.OrderByDescending(g => g.Key.yOrd).ThenByDescending(g => g.Key.mOrd).First();

            var (ly, lm) = latestPeriod.Key;
            var (priorY, priorM) = PreviousCalendarMonth(ly, lm);

            var priorPeriod =
                byPeriod.FirstOrDefault(g => g.Key.yOrd == priorY && g.Key.mOrd == priorM);

            if (priorPeriod == null)
                return false;

            bool PeriodFullyUnpaid(IGrouping<(int yOrd, int mOrd),
                    (int yOrd, int mOrd, MaintenanceBill Bill, string Project, string Phase)> grp) =>
                grp.All(e => IsOutstandingUnpaid(e.Bill));

            if (!PeriodFullyUnpaid(latestPeriod))
                return false;

            if (!PeriodFullyUnpaid(priorPeriod))
                return false;

            var representative = latestPeriod.OrderByDescending(e => e.Bill.DueDate).First();

            static string LabelFrom(IEnumerable<(int yOrd, int mOrd, MaintenanceBill Bill, string Project, string Phase)> grp) =>
                FormatBillingPeriodLabel(grp.First().Bill.BillingMonth, grp.First().Bill.BillingYear);

            var newerLabel = LabelFrom(latestPeriod);
            var olderLabel = LabelFrom(priorPeriod);

            var displayBill = representative.Bill;

            decimal billAmt = (decimal?)displayBill.BillAmountInDueDate ?? 0m;
            decimal amtPaid = (decimal?)displayBill.AmountPaid ?? 0m;
            var outs = billAmt - amtPaid;
            if (outs < 0m)
                outs = 0m;

            row = new OperationsUnpaidRowViewModel
            {
                BtNo = displayBill.Btno?.Trim() ?? "",
                CustomerName = displayBill.CustomerName ?? "",
                Project = representative.Project,
                Phase = representative.Phase,
                BillingMonth = displayBill.BillingMonth ?? "",
                BillingYear = displayBill.BillingYear ?? "",
                DueDate = displayBill.DueDate,
                PaymentStatus = displayBill.PaymentStatus ?? "",
                BillAmount = billAmt,
                AmountPaid = amtPaid,
                OutstandingAmount = outs,
                ConsecutiveUnpaidMonths = $"{olderLabel}, {newerLabel}"
            };

            return true;
        }

        private static readonly Dictionary<string, int> BillingMonthOrdinal = new(StringComparer.OrdinalIgnoreCase)
        {
            ["January"] = 1, ["February"] = 2, ["March"] = 3, ["April"] = 4,
            ["May"] = 5, ["June"] = 6, ["July"] = 7, ["August"] = 8,
            ["September"] = 9, ["October"] = 10, ["November"] = 11, ["December"] = 12,
        };

        private static bool TryParseBillingPeriodOrdinal(string? billingMonth, string? billingYear, out int year,
            out int month)
        {
            year = 0;
            month = 0;
            if (string.IsNullOrWhiteSpace(billingMonth) || string.IsNullOrWhiteSpace(billingYear))
                return false;

            var yTrim = billingYear.Trim();
            if (!int.TryParse(yTrim, out var y))
                return false;

            var mTrim = billingMonth.Trim();
            if (BillingMonthOrdinal.TryGetValue(mTrim, out var mOrdinal))
            {
                year = y;
                month = mOrdinal;
                return true;
            }

            if (int.TryParse(mTrim, out var mNum) && mNum >= 1 && mNum <= 12)
            {
                year = y;
                month = mNum;
                return true;
            }

            if (DateTime.TryParseExact(mTrim, "MMMM", CultureInfo.InvariantCulture, DateTimeStyles.None,
                    out var dt))
            {
                year = y;
                month = dt.Month;
                return true;
            }

            return false;
        }

        private static (int prevYear, int prevMonth) PreviousCalendarMonth(int yr, int monthNum) =>
            monthNum == 1 ? (yr - 1, 12) : (yr, monthNum - 1);

        private static string FormatBillingPeriodLabel(string? billingMonth, string? billingYear) =>
            $"{billingMonth?.Trim()} {billingYear?.Trim()}".Trim();

        private static bool IsOutstandingUnpaid(MaintenanceBill mb)
        {
            decimal billAmt = (decimal?)mb.BillAmountInDueDate ?? 0m;
            decimal amtPaid = (decimal?)mb.AmountPaid ?? 0m;
            var outs = billAmt - amtPaid;
            if (outs <= 0m)
                return false;
            return !IsPaidStatus(mb.PaymentStatus);
        }

        [HttpGet]
        public IActionResult UnpaidList(string? billingMonth, string? billingYear, string? paymentStatus)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var selectedMonth = string.IsNullOrWhiteSpace(billingMonth) ? DateTime.Now.ToString("MMMM") : billingMonth.Trim();
            var selectedYear = string.IsNullOrWhiteSpace(billingYear) ? DateTime.Now.Year.ToString() : billingYear.Trim();
            var selectedPaymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? "Unpaid" : paymentStatus.Trim();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.PaymentStatuses = new List<string> { "Unpaid", "Paid" };
            ViewBag.SelectedBillingMonth = selectedMonth;
            ViewBag.SelectedBillingYear = selectedYear;
            ViewBag.SelectedPaymentStatus = selectedPaymentStatus;

            var model = (from mb in _dbContext.MaintenanceBills.AsNoTracking()
                         join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo into cmGroup
                         from cm in cmGroup.DefaultIfEmpty()
                         where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                         select new OperationsUnpaidRowViewModel
                         {
                             BtNo = mb.Btno ?? "",
                             CustomerName = mb.CustomerName ?? "",
                             Project = cm != null ? (cm.Project ?? "") : (mb.Project ?? ""),
                             Phase = cm != null ? (cm.SubProject ?? "") : (mb.PhaseName ?? ""),
                             BillingMonth = mb.BillingMonth ?? "",
                             BillingYear = mb.BillingYear ?? "",
                             DueDate = mb.DueDate,
                             PaymentStatus = mb.PaymentStatus ?? "",
                             BillAmount = (decimal?)mb.BillAmountInDueDate ?? 0m,
                             AmountPaid = (decimal?)mb.AmountPaid ?? 0m,
                             OutstandingAmount = 0m
                         })
                        .AsEnumerable()
                        .Select(x =>
                        {
                            var outstanding = x.BillAmount - x.AmountPaid;
                            x.OutstandingAmount = outstanding > 0m ? outstanding : 0m;
                            return x;
                        })
                        .Where(x =>
                            selectedPaymentStatus.Equals("Paid", StringComparison.OrdinalIgnoreCase)
                                ? IsPaidStatus(x.PaymentStatus)
                                : (x.OutstandingAmount > 0m && !IsPaidStatus(x.PaymentStatus)))
                        .OrderBy(x => x.Project)
                        .ThenBy(x => x.Phase)
                        .ThenBy(x => x.BtNo)
                        .ToList();

            return View(model);
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

        /// <summary>Projects from Configuration where ConfigKey = "projects".</summary>
        private List<string> GetProjectListFromConfiguration()
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

        /// <summary>Phases from Configuration where ConfigKey matches the selected project name (comma-separated ConfigValue).</summary>
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

        private static bool IsPaidStatus(string? paymentStatus)
        {
            var s = (paymentStatus ?? "").Trim().ToLowerInvariant();
            return s == "paid"
                || s == "paid with surcharge"
                || s == "paidwithsurcharge"
                || s == "partially paid"
                || s == "paritally paid";
        }
    }
}
