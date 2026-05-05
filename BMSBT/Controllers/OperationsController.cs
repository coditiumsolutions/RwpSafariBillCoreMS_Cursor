using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
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
    }
}
