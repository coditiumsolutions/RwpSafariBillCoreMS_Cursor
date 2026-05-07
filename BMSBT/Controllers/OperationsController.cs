using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
using BMSBT.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
        public IActionResult Disconnections(string? billingMonth, string? billingYear)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var currentMonth = string.IsNullOrWhiteSpace(billingMonth) ? DateTime.Now.ToString("MMMM") : billingMonth.Trim();
            var currentYear = string.IsNullOrWhiteSpace(billingYear) ? DateTime.Now.Year.ToString() : billingYear.Trim();
            var today = DateOnly.FromDateTime(DateTime.Today);

            var model = (from mb in _dbContext.MaintenanceBills.AsNoTracking()
                         join cm in _dbContext.CustomersMaintenance.AsNoTracking() on mb.Btno equals cm.BTNo into cmGroup
                         from cm in cmGroup.DefaultIfEmpty()
                         where mb.BillingMonth == currentMonth && mb.BillingYear == currentYear
                         where mb.DueDate != null && mb.DueDate < today
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
                         .Where(x => x.DueDate != null && x.DueDate < today)
                         .Where(x => x.OutstandingAmount > 0m && !IsPaidStatus(x.PaymentStatus))
                         .OrderBy(x => x.Project)
                         .ThenBy(x => x.Phase)
                         .ThenBy(x => x.BtNo)
                         .ToList();

            ViewBag.Months = GetMonthList();
            ViewBag.Years = GetYearList();
            ViewBag.SelectedBillingMonth = currentMonth;
            ViewBag.SelectedBillingYear = currentYear;
            ViewBag.CurrentMonth = currentMonth;
            ViewBag.CurrentYear = currentYear;

            return View(model);
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
