using BMSBT.BillServices;
using BMSBT.Models;
using BMSBT.Requests;
using BMSBT.ViewModels;
using BMSBT.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Text.Json.Nodes;
using X.PagedList;
using X.PagedList.Extensions;
using X.PagedList.Mvc.Core;
using static BMSBT.Controllers.MaintenanceBillController;

namespace BMSBT.Controllers
{
    public class MaintenanceNewController : Controller
    {
        private readonly BmsbtContext _dbContext;
        private readonly MaintenanceFunctions MaintenanceFunctions;
        private readonly ICurrentOperatorService _operatorService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

       
        public MaintenanceNewController(IHttpClientFactory httpClientFactory, BmsbtContext context, ICurrentOperatorService operatorService, IConfiguration configuration)
        {
            _dbContext = context;
            MaintenanceFunctions = new MaintenanceFunctions(_dbContext);
            _operatorService = operatorService;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }






        public async Task<IActionResult> Index(string selectedYear, string selectedMonth, string? apiPathSegment, string? apiProject, string? phaseNumber)
        {
            // Defaults to current month/year if none provided
            if (string.IsNullOrEmpty(selectedYear)) selectedYear = DateTime.Now.Year.ToString();
            if (string.IsNullOrEmpty(selectedMonth)) selectedMonth = DateTime.Now.ToString("MMMM");

            // Retain the selected values
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            var extSection = _configuration.GetSection("ExternalMaintenanceBillsApi");
            var apiBase = extSection["BaseUrl"]?.Trim().TrimEnd('/');
            var pathSeg = string.IsNullOrWhiteSpace(apiPathSegment) ? extSection["DefaultPathSegment"] : apiPathSegment;
            var proj = string.IsNullOrWhiteSpace(apiProject) ? extSection["DefaultProject"] : apiProject;
            var phase = string.IsNullOrWhiteSpace(phaseNumber) ? extSection["DefaultPhaseNumber"] : phaseNumber;
            ViewBag.ApiPathSegment = pathSeg ?? "";
            ViewBag.ApiProject = proj ?? "";
            ViewBag.PhaseNumber = phase ?? "";

            List<Dictionary<string, string>>? apiBillRows = null;
            List<string>? apiBillColumns = null;

            if (!string.IsNullOrEmpty(apiBase) && !string.IsNullOrEmpty(pathSeg))
            {
                var url =
                    $"{apiBase}/api/MaintenanceBills/{Uri.EscapeDataString(pathSeg)}" +
                    $"?project={Uri.EscapeDataString(proj ?? "")}" +
                    $"&phaseNumber={Uri.EscapeDataString(phase ?? "")}" +
                    $"&billingMonth={Uri.EscapeDataString(selectedMonth)}" +
                    $"&billingYear={Uri.EscapeDataString(selectedYear)}";
                ViewBag.ExternalBillsRequestUrl = url;

                try
                {
                    var client = _httpClientFactory.CreateClient();
                    client.Timeout = TimeSpan.FromSeconds(60);
                    var response = await client.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();
                    if (!response.IsSuccessStatusCode)
                    {
                        ViewBag.ExternalBillsError = $"API returned {(int)response.StatusCode}: {body}";
                    }
                    else if (!TryParseMaintenanceBillsJson(body, out var columns, out var rows, out var parseErr))
                    {
                        ViewBag.ExternalBillsError = parseErr ?? "Could not parse API response.";
                    }
                    else
                    {
                        apiBillColumns = columns;
                        apiBillRows = rows;
                        ViewBag.ExternalBillColumns = columns;
                        ViewBag.ExternalBillRows = rows;
                    }
                }
                catch (Exception ex)
                {
                    ViewBag.ExternalBillsError = ex.Message;
                }
            }

            // Display total number of customers and breakdown by project
            var customerCountsByProject = _dbContext.CustomersMaintenance
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

            // Summary from external API rows only (local MaintenanceBills table may not match EF / DB schema).
            if (apiBillRows != null && apiBillColumns != null && apiBillRows.Count > 0)
            {
                SummarizeMaintenanceBillsFromApiRows(extSection, apiBillColumns, apiBillRows, out var totalBills, out var totalAmount, out var paidC, out var paidAmt, out var surC, out var surAmt, out var partC, out var partAmt, out var unC, out var unAmt);
                ViewBag.TotalBills = totalBills;
                ViewBag.TotalAmountGenerated = totalAmount;
                ViewBag.PaidCount = paidC;
                ViewBag.PaidAmount = paidAmt;
                ViewBag.SurchargeCount = surC;
                ViewBag.SurchargeAmount = surAmt;
                ViewBag.PartialCount = partC;
                ViewBag.PartialAmount = partAmt;
                ViewBag.UnpaidBillsCount = unC;
                ViewBag.BillUnpaidAmount = unAmt;
                ViewBag.BillsSummarySource = "api";
            }
            else
            {
                ViewBag.TotalBills = 0;
                ViewBag.TotalAmountGenerated = 0m;
                ViewBag.PaidCount = 0;
                ViewBag.PaidAmount = 0m;
                ViewBag.SurchargeCount = 0;
                ViewBag.SurchargeAmount = 0m;
                ViewBag.PartialCount = 0;
                ViewBag.PartialAmount = 0m;
                ViewBag.UnpaidBillsCount = 0;
                ViewBag.BillUnpaidAmount = 0m;
                ViewBag.BillsSummarySource = apiBillRows != null ? "api" : "none";
            }

            return View();
        }

        /// <summary>
        /// Maps API JSON columns (configurable or auto-detected) to dashboard aggregates.
        /// </summary>
        private static void SummarizeMaintenanceBillsFromApiRows(
            IConfigurationSection extSection,
            List<string> columnOrder,
            List<Dictionary<string, string>> rows,
            out int totalBills,
            out decimal totalAmountGenerated,
            out int paidCount,
            out decimal paidAmount,
            out int surchargeCount,
            out decimal surchargeAmount,
            out int partialCount,
            out decimal partialAmount,
            out int unpaidCount,
            out decimal unpaidAmount)
        {
            totalBills = rows.Count;
            totalAmountGenerated = 0;
            paidCount = surchargeCount = partialCount = unpaidCount = 0;
            paidAmount = surchargeAmount = partialAmount = unpaidAmount = 0m;

            var amountKey = ResolveApiColumnKey(columnOrder, extSection["SummaryColumnBillAmountInDueDate"],
                "BillAmountInDueDate", "billAmountInDueDate", "BillAmount", "Amount", "TotalAmount", "MaintCharges", "maintCharges");
            if (amountKey == null)
            {
                foreach (var c in columnOrder)
                {
                    if (c.Contains("BillAmount", StringComparison.OrdinalIgnoreCase) && c.Contains("Due", StringComparison.OrdinalIgnoreCase))
                    {
                        amountKey = c;
                        break;
                    }
                }
            }

            var statusKey = ResolveApiColumnKey(columnOrder, extSection["SummaryColumnPaymentStatus"],
                "PaymentStatus", "paymentStatus", "Status", "BillStatus", "Payment_State");

            foreach (var row in rows)
            {
                var amt = amountKey != null && row.TryGetValue(amountKey, out var av) ? ParseLooseDecimal(av) : 0m;
                totalAmountGenerated += amt;

                var statusRaw = statusKey != null && row.TryGetValue(statusKey, out var sv) ? sv : null;
                var bucket = ClassifyApiPaymentStatus(statusRaw);
                switch (bucket)
                {
                    case "paid":
                        paidCount++;
                        paidAmount += amt;
                        break;
                    case "surcharge":
                        surchargeCount++;
                        surchargeAmount += amt;
                        break;
                    case "partial":
                        partialCount++;
                        partialAmount += amt;
                        break;
                    default:
                        unpaidCount++;
                        unpaidAmount += amt;
                        break;
                }
            }
        }

        private static string? ResolveApiColumnKey(List<string> columnOrder, string? configuredExact, params string[] fallbacks)
        {
            if (!string.IsNullOrWhiteSpace(configuredExact))
            {
                var hit = columnOrder.FirstOrDefault(c => string.Equals(c, configuredExact.Trim(), StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }
            foreach (var f in fallbacks)
            {
                var hit = columnOrder.FirstOrDefault(c => string.Equals(c, f, StringComparison.OrdinalIgnoreCase));
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static decimal ParseLooseDecimal(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return 0m;
            var t = s.Trim().Replace(",", "");
            return decimal.TryParse(t, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d)
                ? d
                : 0m;
        }

        private static string ClassifyApiPaymentStatus(string? paymentStatus)
        {
            if (string.IsNullOrWhiteSpace(paymentStatus))
                return "unpaid";
            var s = paymentStatus.Trim();
            var t = s.ToLowerInvariant();
            if (t == "paid")
                return "paid";
            if (t.Contains("surcharge", StringComparison.OrdinalIgnoreCase))
                return "surcharge";
            if (t.Contains("partial", StringComparison.OrdinalIgnoreCase))
                return "partial";
            if (t == "unpaid")
                return "unpaid";
            return "unpaid";
        }


        public IActionResult CustomersMaintenance()
        {
            var projects = GetConfigurationCsvValuesByKey("Projects");

            var model = new MaintenanceCustomerFilterViewModel
            {
                Projects = projects,
                Phases = new List<string>(),
                Customers = new List<CustomersMaintenance>().ToPagedList(1, 20)
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult CreateCustomer()
        {
            PopulateCreateCustomerDropdowns(null);
            return View(new CustomersMaintenance());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult CreateCustomer(CustomersMaintenance model)
        {
            if (ModelState.IsValid)
            {
                _dbContext.CustomersMaintenance.Add(model);
                _dbContext.SaveChanges();
                TempData["SuccessMessage"] = $"Customer '{model.CustomerName}' created successfully.";
                return RedirectToAction("CustomersMaintenance");
            }
            PopulateCreateCustomerDropdowns(model.Project);
            return View(model);
        }

        private void PopulateCreateCustomerDropdowns(string? selectedProject)
        {
            // Projects from Configuration
            var projects = _dbContext.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(p => p)
                .Select(p => new SelectListItem { Value = p, Text = p })
                .ToList();
            ViewBag.ProjectList = projects;

            // Phase numbers for the selected project
            var phaseNumbers = new List<SelectListItem>();
            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                phaseNumbers = _dbContext.Configurations
                    .Where(c => c.ConfigKey != null && c.ConfigKey.Trim() == selectedProject.Trim()
                                && !string.IsNullOrWhiteSpace(c.ConfigValue))
                    .Select(c => c.ConfigValue!)
                    .AsEnumerable()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .OrderBy(v => v)
                    .Select(v => new SelectListItem { Value = v, Text = v })
                    .ToList();
            }
            ViewBag.PhaseNameList = phaseNumbers;

            ViewBag.CategoryList = new List<SelectListItem>
            {
                new SelectListItem { Value = "Residential", Text = "Residential" },
                new SelectListItem { Value = "Commercial",  Text = "Commercial"  }
            };

            ViewBag.ConnectionStatusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "Connected",    Text = "Connected"    },
                new SelectListItem { Value = "Disconnected", Text = "Disconnected" }
            };
        }

        [HttpGet]
        public JsonResult GetPhasesByProject(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return Json(new List<string>());

            var phases = GetConfigurationCsvValuesByKey(project.Trim());
            return Json(phases);
        }

        [HttpGet]
        public JsonResult GetBlocksByProject(string project)
        {
            var blocksQuery = _dbContext.CustomersMaintenance.AsQueryable();

            if (!string.IsNullOrWhiteSpace(project))
            {
                blocksQuery = blocksQuery.Where(c => c.Project == project);
            }

            var blocks = blocksQuery
                .Select(c => c.Category)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            return Json(blocks);
        }

        [HttpGet]
        public PartialViewResult FilterCustomers(string project, string phase, string btNo, int? page)
        {
            var query = _dbContext.CustomersMaintenance.AsQueryable();

            if (!string.IsNullOrWhiteSpace(project))
            {
                var selectedProject = project.Trim();
                query = query.Where(c => c.Project != null && c.Project.Trim() == selectedProject);
            }

            if (!string.IsNullOrWhiteSpace(phase))
            {
                var selectedPhase = phase.Trim();
                query = query.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedPhase);
            }

            if (!string.IsNullOrWhiteSpace(btNo))
            {
                query = query.Where(c => c.BTNo != null && c.BTNo.Contains(btNo));
            }

            int pageSize = 20;
            int pageNumber = (page ?? 1);

            var customers = query
                .OrderBy(c => c.Project)
                .ThenBy(c => c.SubProject)
                .ThenBy(c => c.BTNo)
                .ToPagedList(pageNumber, pageSize);

            return PartialView("_MaintenanceCustomersGrid", customers);
        }

        private List<string> GetConfigurationCsvValuesByKey(string configKey)
        {
            var rawValues = _dbContext.Configurations
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
                .OrderBy(v => v)
                .ToList();
        }



        public async Task<IActionResult> GenerateBill(string selectedProject, string selectedPhaseName, string selectedPhaseNumber, string selectedSubProject, string btNoSearch)
        {
            // Backward-compatible aliases for old query keys
            if (string.IsNullOrWhiteSpace(selectedPhaseName) && !string.IsNullOrWhiteSpace(selectedPhaseNumber))
            {
                selectedPhaseName = selectedPhaseNumber;
            }
            if (string.IsNullOrWhiteSpace(selectedPhaseName) && !string.IsNullOrWhiteSpace(selectedSubProject))
            {
                selectedPhaseName = selectedSubProject;
            }
            // Set Operator Name, Billing Month, Billing Year from session and Operators Setup
            string userName = HttpContext.Session.GetString("UserName");
            
            // 1. Force the displayed Operator Name to match the logged-in session user (e.g., "shahid")
            ViewBag.OperatorName = userName;

            if (!string.IsNullOrEmpty(userName))
            {
                // 2. Fetch the billing month and year from the setup that matches this user
                var operatorSetup = _dbContext.OperatorsSetups
                    .AsEnumerable()
                    .FirstOrDefault(o => string.Equals(o.OperatorName?.Trim(), userName.Trim(), StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(o.OperatorID?.Trim(), userName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (operatorSetup != null)
                {
                    ViewBag.BillingMonth = operatorSetup.BillingMonth;
                    ViewBag.BillingYear = operatorSetup.BillingYear;
                }
            }

            // Dropdown projects from Configuration (ConfigKey = "Projects")
            var projects = _dbContext.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            // PhaseNumbers for selected project (Configuration: ConfigKey = selectedProject)
            var phaseNumbers = new List<string>();
            if (!string.IsNullOrEmpty(selectedProject))
            {
                phaseNumbers = _dbContext.Configurations
                    .Where(c => c.ConfigKey != null
                                && c.ConfigKey.Trim() == selectedProject.Trim()
                                && !string.IsNullOrWhiteSpace(c.ConfigValue))
                    .Select(c => c.ConfigValue!)
                    .AsEnumerable()
                    .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    .Select(v => v.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct()
                    .OrderBy(v => v)
                    .ToList();
            }

            // Start with empty result
            var filteredData = new List<MaintSectorCustomersViewModel>();

            // Only load if project is selected
            if (!string.IsNullOrEmpty(selectedProject))
            {
                var query = _dbContext.CustomersMaintenance
                    .Where(c => c.Project != null && c.Project.Trim() == selectedProject.Trim());

                if (!string.IsNullOrEmpty(selectedPhaseName))
                {
                    query = query.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedPhaseName.Trim());
                }

                if (!string.IsNullOrEmpty(btNoSearch))
                {
                    query = query.Where(c => c.BTNo != null && c.BTNo.Contains(btNoSearch));
                }

                filteredData = query.GroupBy(c => c.Category)
                .Select(g => new MaintSectorCustomersViewModel
                {
                    Block = g.Key,
                    Customers = g.ToList()
                 })
                .OrderBy(g => g.Block)
                .ToList();

            }

            ViewBag.Projects = projects;
            ViewBag.PhaseNames = phaseNumbers;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhaseName = selectedPhaseName;

            return View(filteredData);

        }




        [HttpPost]
        
        public async Task<IActionResult> GenerateMaintenanceBills([FromBody] MaintenanceBillRequest request)
        {
            // Set Operator Name
            string operatorId = HttpContext.Session.GetString("OperatorId");
            await _operatorService.InitializeAsync(operatorId);
            var currentOperator = _operatorService.GetCurrentOperator();

            // Check if CurrentMonth and CurrentYear are set
            if (string.IsNullOrEmpty(currentOperator.BillingMonth) || string.IsNullOrEmpty(currentOperator.BillingYear))
            {
                return Json(new { success = false, message = "Please Update Operator Setup" });
            }

            if (string.IsNullOrEmpty(operatorId))
            {
                return Json(new { success = false, message = "Operator ID not found in session" });
            }

            if (currentOperator == null)
            {
                return Json(new { success = false, message = "Operator details not found" });
            }



            string billingMonth = currentOperator.BillingMonth;
            string billingYear = currentOperator.BillingYear.ToString();

            if (string.IsNullOrEmpty(billingMonth) || string.IsNullOrEmpty(billingYear))
            {
                return Json(new { success = false, message = "Month and Year must be provided." });
            }


            MaintenanceFunctions.GetPreviousBillingPeriod(billingMonth, billingYear);
            string previousMonth = BillCreationState.PreviousMonth;
            string previousYear = BillCreationState.PreviousYear;
            DateOnly? IssueDate = currentOperator.IssueDate.HasValue
       ? DateOnly.FromDateTime(currentOperator.IssueDate.Value)
       : (DateOnly?)null;

            DateOnly? DueDate = currentOperator.DueDate.HasValue
                ? DateOnly.FromDateTime(currentOperator.DueDate.Value)
                : (DateOnly?)null;


            var results = new List<string>();

            // Generate bills for each selected customer ID
            foreach (var customerId in request.SelectedIds)
            {
                // Call the function to generate the bill for each customer
                var result = MaintenanceFunctions.GenerateBillForCustomer(customerId, billingMonth, billingYear, previousMonth, previousYear, IssueDate, DueDate);
                results.Add(result);
            }

            // Return a success message with the generated results
            return Json(new { success = true, message = "Results generated successfully!", results });
        }

        [HttpPost]
        public async Task<IActionResult> InitializeCustomers([FromBody] MaintenanceBillRequest request)
        {
            var selectedIds = request?.SelectedIds ?? new List<int>();
            if (!selectedIds.Any())
            {
                return Json(new { success = false, message = "No customers selected." });
            }

            var customers = await _dbContext.CustomersMaintenance
                .Where(c => selectedIds.Contains(c.Uid))
                .ToListAsync();

            if (!customers.Any())
            {
                return Json(new { success = false, message = "No matching customers found." });
            }

            foreach (var customer in customers)
            {
                customer.BillGenerationStatus = "Not Generated";
            }

            await _dbContext.SaveChangesAsync();

            var updates = customers.Select(c => new
            {
                uid = c.Uid,
                status = c.BillGenerationStatus
            }).ToList();

            return Json(new
            {
                success = true,
                message = $"Initialized {customers.Count} customer(s).",
                updates
            });
        }







        public IActionResult Generate(string project = null, string plotType = null, string plotSize = null)
        {
            var customers = _dbContext.CustomersDetails.AsQueryable();

            if (!string.IsNullOrEmpty(project))
            {
                customers = customers.Where(c => c.Project == project);
            }

            if (!string.IsNullOrEmpty(plotType))
            {
                customers = customers.Where(c => c.PlotType == plotType);
            }

            if (!string.IsNullOrEmpty(plotSize))
            {
                customers = customers.Where(c => c.Size == plotSize);
            }

            return View(customers.ToList());
        }





        //[HttpGet] // Changed from [HttpPost]
        //public IActionResult MaintenanceBillsSearch(string billingMonth, string billingYear, string block, string btNo, int? page)
        //{
        //    ViewBag.Months = GetMonths();
        //    ViewBag.Years = GetYears();

        //    var query = _dbContext.MaintenanceBills.AsQueryable();
        //    bool hasFilter = false;

        //    if (!string.IsNullOrEmpty(billingMonth))
        //    {
        //        query = query.Where(x => x.BillingMonth == billingMonth);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(billingYear))
        //    {
        //        query = query.Where(x => x.BillingYear == billingYear);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(block))
        //    {
        //        query = query.Where(x => x.Btno == block);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(btNo))
        //    {
        //        query = query.Where(x => x.Btno == btNo);
        //        hasFilter = true;
        //    }

        //    const int pageSize = 50;
        //    var pageNumber = page ?? 1;

        //    var totalRecords = query.Count();
        //    var items = query.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        //    // Always show grid if there are records, regardless of filters
        //    ViewBag.ShowGrid = items.Any() || hasFilter || pageNumber > 1;

        //    return View(new PaginationViewModel<MaintenanceBill>
        //    {
        //        Items = items,
        //        PageNumber = pageNumber,
        //        PageSize = pageSize,
        //        TotalRecords = totalRecords
        //    });
        //}

        /// <summary>
        /// Bills summary by project: one table with Project, Customers (from CustomersMaintenance), Bills Generated (for selected month/year).
        /// </summary>
        [HttpGet]
        public IActionResult BillsSummary(string? month, string? year)
        {
            ViewBag.Months = GetMonths();
            ViewBag.Years = GetYears();
            ViewBag.SelectedMonth = month ?? DateTime.Now.ToString("MMMM");
            ViewBag.SelectedYear = year ?? DateTime.Now.Year.ToString();

            var selectedMonth = ViewBag.SelectedMonth as string;
            var selectedYear = ViewBag.SelectedYear as string;

            // Customers count per project (from CustomersMaintenance)
            var customerSummaryByProject = _dbContext.CustomersMaintenance
                .GroupBy(c => c.Project)
                .Select(g => new { Project = g.Key ?? "", Customers = g.Count() })
                .OrderBy(x => x.Project)
                .ToList();

            // Bills count per project for selected month/year
            var billsByProject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(selectedMonth) && !string.IsNullOrEmpty(selectedYear))
            {
                var billsQuery = from mb in _dbContext.MaintenanceBills
                                 join cm in _dbContext.CustomersMaintenance on mb.Btno equals cm.BTNo
                                 where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                                 group mb by cm.Project into g
                                 select new { Project = g.Key ?? "", Count = g.Count() };
                foreach (var x in billsQuery)
                {
                    billsByProject[x.Project] = x.Count;
                }
            }

            // Combined: one row per project with Project, Customers, BillsGenerated
            var combined = customerSummaryByProject
                .Select(c => new BillsSummaryCombinedViewModel
                {
                    Project = c.Project,
                    Customers = c.Customers,
                    BillsGenerated = billsByProject.TryGetValue(c.Project, out var bills) ? bills : 0
                })
                .ToList();

            return View(combined);
        }

        /// <summary>
        /// AJAX endpoint: returns combined bills summary (Project, Customers, BillsGenerated) for the given month/year.
        /// </summary>
        [HttpGet]
        public IActionResult GetBillsSummaryData(string month, string year)
        {
            var customerSummaryByProject = _dbContext.CustomersMaintenance
                .GroupBy(c => c.Project)
                .Select(g => new { Project = g.Key ?? "", Customers = g.Count() })
                .OrderBy(x => x.Project)
                .ToList();

            var billsByProject = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
            {
                var billsQuery = from mb in _dbContext.MaintenanceBills
                                 join cm in _dbContext.CustomersMaintenance on mb.Btno equals cm.BTNo
                                 where mb.BillingMonth == month && mb.BillingYear == year
                                 group mb by cm.Project into g
                                 select new { Project = g.Key ?? "", Count = g.Count() };
                foreach (var x in billsQuery)
                {
                    billsByProject[x.Project] = x.Count;
                }
            }

            var combined = customerSummaryByProject
                .Select(c => new BillsSummaryCombinedViewModel
                {
                    Project = c.Project,
                    Customers = c.Customers,
                    BillsGenerated = billsByProject.TryGetValue(c.Project, out var bills) ? bills : 0
                })
                .ToList();

            return Json(combined);
        }

        /// <summary>
        /// Accepts a JSON array of objects, or an object with data/items/value/result/records array.
        /// </summary>
        private static bool TryParseMaintenanceBillsJson(string json, out List<string> columns, out List<Dictionary<string, string>> rows, out string? error)
        {
            columns = new List<string>();
            rows = new List<Dictionary<string, string>>();
            error = null;
            try
            {
                var node = JsonNode.Parse(json);
                JsonArray? arr = node as JsonArray;
                if (arr == null && node is JsonObject jo)
                {
                    foreach (var key in new[] { "data", "items", "value", "result", "records" })
                    {
                        if (jo[key] is JsonArray ja)
                        {
                            arr = ja;
                            break;
                        }
                    }
                }

                if (arr == null && node is JsonObject singleRow)
                {
                    var dict = FlattenJsonObjectToRow(singleRow, out var keys);
                    if (dict.Count == 0)
                    {
                        error = "JSON object had no properties to display.";
                        return false;
                    }
                    columns = keys;
                    rows.Add(dict);
                    return true;
                }

                if (arr == null)
                {
                    error = "Expected a JSON array of bill rows, a single object, or a wrapper with data/items/value/result/records.";
                    return false;
                }

                var keyOrder = new List<string>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in arr)
                {
                    if (item is not JsonObject rowObj)
                        continue;
                    var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in rowObj)
                    {
                        var text = JsonValueToDisplayString(prop.Value);
                        dict[prop.Key] = text;
                        if (seen.Add(prop.Key))
                            keyOrder.Add(prop.Key);
                    }
                    if (dict.Count > 0)
                        rows.Add(dict);
                }

                columns = keyOrder;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static string JsonValueToDisplayString(JsonNode? n)
        {
            if (n == null) return "";
            if (n is JsonValue v)
            {
                if (v.TryGetValue<bool>(out var b)) return b.ToString();
                if (v.TryGetValue<int>(out var i)) return i.ToString();
                if (v.TryGetValue<long>(out var l)) return l.ToString();
                if (v.TryGetValue<double>(out var d)) return d.ToString("G");
                if (v.TryGetValue<decimal>(out var m)) return m.ToString("G");
                return v.ToString()?.Trim('"') ?? "";
            }
            return n.ToJsonString();
        }

        private static Dictionary<string, string> FlattenJsonObjectToRow(JsonObject rowObj, out List<string> keyOrder)
        {
            keyOrder = new List<string>();
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in rowObj)
            {
                dict[prop.Key] = JsonValueToDisplayString(prop.Value);
                keyOrder.Add(prop.Key);
            }
            return dict;
        }

        private List<string> GetMonths()
        {
            return new List<string> { "January", "February", "March", "April", "May", "June", "July",
                              "August", "September", "October", "November", "December" };
        }

        private List<string> GetYears()
        {
            return new List<string> { "2024", "2025", "2026" };
        }




        //[HttpGet]
        //public IActionResult MaintenanceBillsSearch(string billingMonth, string billingYear, string block, string btNo, int? page)
        //{
        //    ViewBag.Months = GetMonths();
        //    ViewBag.Years = GetYears();

        //    // Start with a join between MaintenanceBills and CustomersDetail
        //    //var query = from mb in _dbContext.MaintenanceBills
        //    //            join cd in _dbContext.CustomersDetails on mb.Btno equals cd.Btno into customerJoin
        //    //            from customer in customerJoin.DefaultIfEmpty() // Left join
        //    //            select new
        //    //            {
        //    //                MaintenanceBill = mb,
        //    //                CustomerBlock = customer.Block
        //    //            };


        //    var query = from mb in _dbContext.MaintenanceBills
        //                join cd in _dbContext.CustomersDetails on mb.Btno equals cd.Btno into customerJoin
        //                from customer in customerJoin.DefaultIfEmpty()
        //                select new MaintenanceBillViewModel  // Using ViewModel
        //                {
        //                    MaintenanceBill = mb,
        //                    Block = customer.Block
        //                };

        //    bool hasFilter = false;

        //    if (!string.IsNullOrEmpty(billingMonth))
        //    {
        //        query = query.Where(x => x.MaintenanceBill.BillingMonth == billingMonth);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(billingYear))
        //    {
        //        query = query.Where(x => x.MaintenanceBill.BillingYear == billingYear);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(block))
        //    {
        //        query = query.Where(x => x.Block == block);
        //        hasFilter = true;
        //    }

        //    if (!string.IsNullOrEmpty(btNo))
        //    {
        //        query = query.Where(x => x.MaintenanceBill.Btno == btNo);
        //        hasFilter = true;
        //    }

        //    const int pageSize = 50;
        //    var pageNumber = page ?? 1;

        //    // Get total count before pagination
        //    var totalRecords = query.Count();

        //    // Apply pagination and select only the MaintenanceBill entities
        //    var items = query
        //        .Skip((pageNumber - 1) * pageSize)
        //        .Take(pageSize)
        //        .Select(x => x.MaintenanceBill)
        //        .ToList();

        //    ViewBag.ShowGrid = hasFilter || pageNumber > 1;

        //    return View(new PaginationViewModel<MaintenanceBillViewModel>
        //    {
        //        Items = items,
        //        PageNumber = pageNumber,
        //        PageSize = pageSize,
        //        TotalRecords = totalRecords
        //    });
        //}


        [HttpGet]
        public IActionResult MaintenanceBillsSearch(string billingMonth, string billingYear, string block, string btNo, int? page)
        {
            ViewBag.Months = GetMonths();
            ViewBag.Years = GetYears();


               ViewBag.Blocks = _dbContext.CustomersMaintenance
                .Select(c => c.Category)
                .Where(b => !string.IsNullOrEmpty(b))
                .Distinct()
                .OrderBy(b => b)
                .ToList();
            ViewBag.SelectedBlock = block; // This comes from your action parameter


            // Check if all filter parameters are empty
            bool noFilterSelected = string.IsNullOrEmpty(billingMonth) &&
                                    string.IsNullOrEmpty(billingYear) &&
                                    string.IsNullOrEmpty(block) &&
                                    string.IsNullOrEmpty(btNo);

            // Set ViewBag message and empty grid if no filter is selected
            if (noFilterSelected)
            {
                ViewBag.Message = "Please select bill generation criteria.";
                ViewBag.ShowGrid = false;

                return View(new PaginationViewModel<MaintenanceBillViewModel>
                {
                    Items = new List<MaintenanceBillViewModel>(),
                    PageNumber = 1,
                    PageSize = 50,
                    TotalRecords = 0
                });
            }




            //var baseQuery = from mb in _dbContext.MaintenanceBills
            //                join cd in _dbContext.CustomersMaintenance on mb.Btno equals cd.Btno
            //                select new { mb, cd };

            var baseQuery = from mb in _dbContext.MaintenanceBills
                            join cm in _dbContext.CustomersMaintenance on mb.Btno equals cm.BTNo
                            select new { mb, cm };


            // Apply filters
            if (!string.IsNullOrEmpty(billingMonth))
            {
                baseQuery = baseQuery.Where(x => x.mb.BillingMonth == billingMonth);
            }

            if (!string.IsNullOrEmpty(billingYear))
            {
                baseQuery = baseQuery.Where(x => x.mb.BillingYear == billingYear);
            }

            if (!string.IsNullOrEmpty(block))
            {
                baseQuery = baseQuery.Where(x => x.cm.Category == block);
            }

            if (!string.IsNullOrEmpty(btNo))
            {
                baseQuery = baseQuery.Where(x => x.mb.Btno == btNo);
            }

            var query = baseQuery.Select(x => new MaintenanceBillViewModel
            {
                Uid = x.mb.Uid, // ✅ Make sure mb.Uid is correctly mapped
                InvoiceNo = x.mb.InvoiceNo,
                CustomerName = x.mb.CustomerName,
                Btno = x.mb.Btno,
                BillingMonth = x.mb.BillingMonth,
                BillingYear = x.mb.BillingYear,
                BillAmountInDueDate = x.mb.BillAmountInDueDate,
                BillAmountAfterDueDate = x.mb.BillAmountAfterDueDate,
                PaymentStatus = x.mb.PaymentStatus,
                Block = x.cm.Category,
                DueDate = x.mb.DueDate,
              
                //DueDate = x.mb.DueDate.HasValue
                //    ? x.mb.DueDate.Value.ToString("dd/MM/yyyy")
                //    : null // Format the DueDate as "dd/MM/yyyy"        

            });

            const int pageSize = 50;
            var pageNumber = page ?? 1;

            var totalRecords = query.Count();
            var items = query.Skip((pageNumber - 1) * pageSize)
                            .Take(pageSize)
                            .ToList();

            ViewBag.ShowGrid = items.Any() ||
                             !string.IsNullOrEmpty(billingMonth) ||
                             !string.IsNullOrEmpty(billingYear) ||
                             !string.IsNullOrEmpty(block) ||
                             !string.IsNullOrEmpty(btNo);

            return View(new PaginationViewModel<MaintenanceBillViewModel>
            {
                Items = items,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            });
        }





        public IActionResult Details(int id)
        {
            var bill = _dbContext.MaintenanceBills.FirstOrDefault(x => x.Uid == id);
            if (bill == null)
            {
                return NotFound();
            }

            return View(bill);
        }

        [HttpGet]
        public IActionResult Edit(int id)
        {
            var bill = _dbContext.MaintenanceBills.Find(id);
            //var bill = _dbContext.MaintenanceBills.FirstOrDefault(x => x.Uid == id);
            if (bill == null)
            {
                return NotFound();
            }

            // Load Block options from CustomersMaintenance
            ViewBag.BlockList = _dbContext.CustomersMaintenance
                .Select(x => x.Category)
                .Distinct()
                .OrderBy(x => x)
                .ToList();

            return View(bill);
        }



        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public IActionResult Edit(int id, MaintenanceBill updatedBill)
        //{

        //    if (id != updatedBill.Uid)
        //    {
        //        return BadRequest();
        //    }

        //    if (!ModelState.IsValid)
        //    {
        //        return View(updatedBill);
        //    }

        //    var existingBill = _dbContext.MaintenanceBills.FirstOrDefault(x => x.Uid == id);
        //    if (existingBill == null)
        //    {
        //        return NotFound();
        //    }

        //    // Update properties
        //    existingBill.CustomerName = updatedBill.CustomerName;
        //    existingBill.Btno = updatedBill.Btno;
        //    existingBill.BillingMonth = updatedBill.BillingMonth;
        //    existingBill.BillingYear = updatedBill.BillingYear;
        //    existingBill.BillAmountInDueDate = updatedBill.BillAmountInDueDate;
        //    existingBill.BillAmountAfterDueDate = updatedBill.BillAmountAfterDueDate;
        //    existingBill.PaymentStatus = updatedBill.PaymentStatus;
        //    existingBill.LastUpdated = DateTime.Now;

        //    _dbContext.SaveChanges();

        //    return RedirectToAction(nameof(MaintenanceBillsSearch));
        //}


        ////Working
        //[HttpPost]
        //[ValidateAntiForgeryToken]
        //public async Task<IActionResult> Edit(MaintenanceBill bill)
        //{
        //    if (!ModelState.IsValid)
        //        return View(bill);

        //    var existingBill = await _dbContext.MaintenanceBills.FindAsync(bill.Uid);
        //    if (existingBill == null)
        //        return NotFound();

        //    // Only update DueDate
        //    if (existingBill.DueDate != bill.DueDate)
        //    {
        //        string user = HttpContext.Session.GetString("Username") ?? "Unknown User";
        //        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        //        string newEntry = $"DueDate updated to {bill.DueDate:yyyy-MM-dd} by {user} at {timestamp}";

        //        // Append to history
        //        if (!string.IsNullOrEmpty(existingBill.History))
        //        {
        //            existingBill.History += Environment.NewLine + newEntry;
        //        }
        //        else
        //        {
        //            existingBill.History = newEntry;
        //        }

        //        existingBill.DueDate = bill.DueDate;
        //    }

        //    // Save changes
        //    await _dbContext.SaveChangesAsync();
        //    return RedirectToAction("MaintenanceBillsSearch");
        //}


        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(MaintenanceBill model, string action)
        {
            var bill = await _dbContext.MaintenanceBills.FindAsync(model.Uid);
            if (bill == null) return NotFound();

            string user = HttpContext.Session.GetString("Username") ?? "Unknown User";
            string timestamp = DateTime.Now.ToString("dd-MMM-yyyy HH:mm");

            if (action == "delete")
            {
                // Soft delete
                if (!bill.Btno.EndsWith("-Delete"))
                {
                    bill.Btno += "-Delete";
                    bill.BillingMonth += "-Delete";
                }

                bill.History += Environment.NewLine + $"Soft deleted by {user} on {timestamp}";

                await _dbContext.SaveChangesAsync();
                return RedirectToAction("MaintenanceBillsSearch");
            }

            if (action == "update")
            {
                if (bill.DueDate != model.DueDate)
                {
                    bill.History += Environment.NewLine + $"DueDate updated from {bill.DueDate:dd-MMM-yyyy} to {model.DueDate:dd-MMM-yyyy} by {user} on {timestamp}";
                    bill.DueDate = model.DueDate;
                }

                await _dbContext.SaveChangesAsync();
                return RedirectToAction("MaintenanceBillsSearch");
            }

            return View(model);
        }














        /// <summary>
        /// All Bill: Project + optional Phase + Billing Month + Billing Year filters, or BTNo/CustomerName search.
        /// </summary>
        [HttpGet]
        public IActionResult AllBills(string project, string phase, string subProject, string month, string year, string custNoOrName, int? page)
        {
            if (string.IsNullOrWhiteSpace(phase) && !string.IsNullOrWhiteSpace(subProject))
            {
                // Backward compatibility for old querystring key.
                phase = subProject;
            }

            var projects = GetConfigurationCsvValuesByKey("Projects");
            ViewBag.Projects = projects ?? new List<string>();
            ViewBag.SelectedProject = project ?? "";
            ViewBag.SelectedPhase = phase ?? "";
            ViewBag.SelectedMonth = month ?? "";
            ViewBag.SelectedYear = year ?? "";
            ViewBag.CustNoOrName = custNoOrName ?? "";

            const int pageSize = 20;
            var pageNumber = page ?? 1;
            if (pageNumber < 1) pageNumber = 1;

            var list = new List<MaintenanceBillViewModel>();
            var totalRecords = 0;

            var term = custNoOrName?.Trim();
            var hasTerm = !string.IsNullOrWhiteSpace(term);
            var hasAnyFilter = !string.IsNullOrWhiteSpace(project) || !string.IsNullOrWhiteSpace(month) || !string.IsNullOrWhiteSpace(year);
            var hasAllDateFilters = !string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(month) && !string.IsNullOrWhiteSpace(year);
            ViewBag.IncompleteFilterSelection = !hasTerm && hasAnyFilter && !hasAllDateFilters;

            if (hasTerm || hasAllDateFilters)
            {
                var query = from mb in _dbContext.MaintenanceBills
                            join cm in _dbContext.CustomersMaintenance on mb.Btno equals cm.BTNo into cmGroup
                            from cm in cmGroup.DefaultIfEmpty()
                            where hasTerm
                                ? ((mb.Btno != null && mb.Btno.Contains(term!))
                                   || (mb.CustomerName != null && mb.CustomerName.Contains(term!)))
                                : (cm != null
                                   && cm.Project == project
                                   && mb.BillingMonth == month
                                   && mb.BillingYear == year
                                   && (string.IsNullOrWhiteSpace(phase) || cm.SubProject == phase))
                            select new MaintenanceBillViewModel
                            {
                                Uid = mb.Uid,
                                CustomerNo = cm != null ? cm.CustomerNo : "",
                                InvoiceNo = mb.InvoiceNo,
                                CustomerName = mb.CustomerName,
                                Btno = mb.Btno,
                                BillingMonth = mb.BillingMonth,
                                BillingYear = mb.BillingYear,
                                BillAmountInDueDate = mb.BillAmountInDueDate,
                                BillAmountAfterDueDate = mb.BillAmountAfterDueDate,
                                PaymentStatus = mb.PaymentStatus,
                                Block = cm != null ? cm.Category : "",
                                DueDate = mb.DueDate
                            };

                totalRecords = query.Count();
                list = query.OrderBy(x => x.CustomerNo)
                    .ThenBy(x => x.Btno)
                    .Skip((pageNumber - 1) * pageSize)
                    .Take(pageSize)
                    .ToList();
            }

            var model = new PaginationViewModel<MaintenanceBillViewModel>
            {
                Items = list,
                PageNumber = pageNumber,
                PageSize = pageSize,
                TotalRecords = totalRecords
            };
            return View(model);
        }

        /// <summary>
        /// Printable maintenance bill (no layout). Open in new tab from AllBills double-click.
        /// </summary>
        [HttpGet]
        public IActionResult Print(int id, string? project = null)
        {
            var bill = _dbContext.MaintenanceBills.Find(id);
            if (bill == null)
                return NotFound();
            ViewBag.Project = project ?? "";
            return View("PrintBill", bill);
        }

        [Route("PrintMMultiBills")]
        [HttpGet]
        public async Task<IActionResult> PrintMMultiBills()
        {
            var projects = _dbContext.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (projects == null || !projects.Any())
            {
                projects = new List<string>();
            }

            ViewBag.Projects = projects ?? new List<string>();

            return View();
        }

        /// <summary>
        /// JSON: subprojects from Configuration where ConfigKey = selected Project.
        /// Supports both one-value-per-row and comma-separated ConfigValue entries.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetSubProjectPhaseNumbers([FromQuery] string? project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return Json(new List<string>());

            var pNorm = project.Trim();

            var raw = await _dbContext.Configurations
                .AsNoTracking()
                .Where(c => c.ConfigKey != null && c.ConfigKey.Trim() == pNorm && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .ToListAsync();

            var list = raw
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();

            return Json(list);
        }

        [Route("PrintMMultiBills")]
        [HttpPost]
        public async Task<IActionResult> PrintMMultiBills([FromBody] PrintBillRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.project) ||
                    string.IsNullOrWhiteSpace(request.month) ||
                    string.IsNullOrWhiteSpace(request.year))
                {
                    return BadRequest("Project, Billing Month and Billing Year are required.");
                }

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                var selectedProject = request.project.Trim();
                var selectedPhaseName = request.subProject?.Trim();
                var billingMonth = request.month.Trim();
                var billingYear = request.year.Trim();

                // Project-to-API mapping as requested.
                var projectMappings = new Dictionary<string, (string pathSegment, string apiProject, string defaultPhase)>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Safari-1", ("Safari-1", "Safari-1", "Safari Villas") },
                    { "Safari-2", ("Safari-2", "Safari-2", "Safari II") },
                    { "Bahria Spring", ("Safari-3", "Safari-3", "Bahria Springs Commercial Close") },
                    { "Bahria Heights-1", ("SafariHeights", "Bahria Heights-1", "Bahria Heights Ext.1") }
                };

                if (!projectMappings.TryGetValue(selectedProject, out var map))
                {
                    return BadRequest($"Unsupported project '{selectedProject}'.");
                }

                var phaseName = string.IsNullOrWhiteSpace(selectedPhaseName) ? map.defaultPhase : selectedPhaseName;
                var baseUrl = $"http://172.20.229.2:84/api/MaintenanceBills/{Uri.EscapeDataString(map.pathSegment)}";

                var url =
                    $"{baseUrl}?project={Uri.EscapeDataString(map.apiProject)}" +
                    $"&phaseName={Uri.EscapeDataString(phaseName)}" +
                    $"&billingMonth={Uri.EscapeDataString(billingMonth)}" +
                    $"&billingYear={Uri.EscapeDataString(billingYear)}";

                var response = await client.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var pdfData = await response.Content.ReadAsByteArrayAsync();

                    if (pdfData == null || pdfData.Length == 0)
                    {
                        return BadRequest("Received empty PDF data");
                    }

                    Response.Headers.Add("Content-Disposition", "attachment; filename=MaintenanceBill.pdf");
                    return File(pdfData, "application/pdf");
                }

                var errorContent = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, errorContent);
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException?.Message;
                return StatusCode(500, $"Internal server error: {ex.Message}" + (innerMsg != null ? $" | {innerMsg}" : ""));
            }
        }






        [Route("SSQCursorPrintMMultiBills")]
        [HttpPost]
        public async Task<IActionResult> SSQCursorPrintMMultiBills(
        [FromBody] SSQCursorPrintBillRequest request)
        {
            try
            {
                // ✅ Validate required parameters
                if (string.IsNullOrWhiteSpace(request.month) ||
                    string.IsNullOrWhiteSpace(request.year) ||
                    string.IsNullOrWhiteSpace(request.btNo))
                {
                    return BadRequest("BillingMonth, BillingYear and BTNo are required.");
                }

                Console.WriteLine(
                    $"SSQ Cursor Print → BTNo: {request.btNo}, Month: {request.month}, Year: {request.year}"
                );

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Accept
                      .Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                // ✅ SAFELY ENCODE PARAMETERS
                var url =
                    $"https://localhost:7077/api/SSQCursorMaintenance/GetMBill" +
                    $"?BillingMonth={Uri.EscapeDataString(request.month)}" +
                    $"&BillingYear={Uri.EscapeDataString(request.year)}" +
                    $"&BTNo={Uri.EscapeDataString(request.btNo)}";

                var response = await client.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"API Error: {errorContent}");
                }

                var pdfData = await response.Content.ReadAsByteArrayAsync();

                if (pdfData == null || pdfData.Length == 0)
                {
                    return BadRequest("Received empty PDF data.");
                }

                Response.Headers.Add(
                    "Content-Disposition",
                    $"attachment; filename=MaintenanceBill_{request.btNo}.pdf"
                );

                return File(pdfData, "application/pdf");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }







        public IActionResult SearchBill(string? month, string? year, string? BtNo)
        {
            ViewBag.SelectedMonth = month;
            ViewBag.SelectedYear = year;
            ViewBag.SelectedBtNo = BtNo;

            // If nothing is provided
            if (string.IsNullOrEmpty(BtNo) && string.IsNullOrEmpty(month) && string.IsNullOrEmpty(year))
            {
                return View("SearchBill");
            }

            var query = from bill in _dbContext.MaintenanceBills
                        join customer in _dbContext.CustomersMaintenance
                            on bill.Btno equals customer.BTNo into customerJoin
                        from customer in customerJoin.DefaultIfEmpty()
                        select new MaintenanceBillDTO
                        {
                            Uid = bill.Uid,
                            CustomerNo = bill.CustomerNo ?? (customer != null ? customer.CustomerNo : ""),
                            Btno = bill.Btno,
                            CustomerName = bill.CustomerName ?? (customer != null ? customer.CustomerName : ""),
                            Cnicno = customer != null ? customer.CNICNo : "",
                            FatherName = customer != null ? customer.FatherName : "",
                            InstalledOn = customer != null ? customer.InstalledOn : "",
                            MobileNo = customer != null ? customer.MobileNo : "",
                            TelephoneNo = customer != null ? customer.TelephoneNo : "",
                            Ntnnumber = customer != null ? customer.NTNNumber : "",
                            City = customer != null ? customer.City : "",
                            Project = customer != null ? customer.Project : "",
                            SubProject = customer != null ? customer.SubProject : "",
                            TariffName = customer != null ? customer.TariffName : "",
                            BankNo = customer != null ? customer.BankNo : "",
                            BtnoMaintenance = customer != null ? customer.BTNo : "",
                            Category = customer != null ? customer.Category : "",
                            Block = customer != null ? customer.Category : "",
                            PlotType = customer != null ? customer.PlotStatus : "",
                            Size = customer != null ? customer.Size : "",
                            Sector = customer != null ? customer.Sector : "",
                            PloNo = customer != null ? customer.PloNo : "",
                            BillStatusMaint = customer != null ? customer.BillStatusMaint : "",
                            BillStatus = customer != null ? customer.BillStatus : "",
                            InvoiceNo = bill.InvoiceNo,
                            BillingMonth = bill.BillingMonth,
                            BillingYear = bill.BillingYear,
                            BillingDate = bill.BillingDate,
                            DueDate = bill.DueDate,
                            IssueDate = bill.IssueDate,
                            ValidDate = bill.ValidDate,
                            PaymentStatus = bill.PaymentStatus,
                            PaymentDate = bill.PaymentDate,
                            PaymentMethod = bill.PaymentMethod,
                            BankDetail = bill.BankDetail,
                            TaxAmount = bill.TaxAmount,
                            BillAmountInDueDate = bill.BillAmountInDueDate,
                            BillSurcharge = bill.BillSurcharge,
                            BillAmountAfterDueDate = bill.BillAmountAfterDueDate,
                            MaintCharges = bill.MaintCharges,
                            Arrears = bill.Arrears
                        };

            // Apply filters based on inputs
            if (!string.IsNullOrEmpty(BtNo))
            {
                var trimmedBtNo = BtNo.Trim();
                query = query.Where(b => b.Btno == trimmedBtNo);

                if (!string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
                {
                    query = query.Where(b => b.BillingMonth == month && b.BillingYear == year);
                }
            }
            else if (!string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
            {
                // BtNo is empty, filter by month/year only
                query = query.Where(b => b.BillingMonth == month && b.BillingYear == year);
            }

            var billsList = query.ToList();

            if (!billsList.Any())
            {
                ViewBag.ErrorMessage = "No bills found for the provided criteria.";
            }

            var pagedBills = billsList.ToPagedList(1, 5000);
            return View("SearchBill", pagedBills);
        }

        [HttpGet]
        public IActionResult Operations(string? month, string? year, string? btno)
        {
            var model = new MaintenanceOperationsViewModel
            {
                BillingMonth = month,
                BillingYear = year,
                Btno = btno
            };

            if (!string.IsNullOrEmpty(btno) && !string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
            {
                var bill = _dbContext.MaintenanceBills
                    .FirstOrDefault(b => b.Btno == btno.Trim() && b.BillingMonth == month && b.BillingYear == year);

                if (bill != null)
                {
                    model.Bill = bill;
                    model.Customer = _dbContext.CustomersMaintenance
                        .FirstOrDefault(c => c.BTNo == btno.Trim());
                }
                else
                {
                    model.ErrorMessage = "No Bill Found";
                }
            }

            return View(model);
        }

        [HttpPost]
        public IActionResult UpdatePaymentStatus(int billUid, string status)
        {
            var bill = _dbContext.MaintenanceBills.Find(billUid);
            if (bill != null)
            {
                bill.PaymentStatus = status;
                bill.LastUpdated = DateTime.Now;
                _dbContext.SaveChanges();
                TempData["SuccessMessage"] = "Payment status updated successfully";

                return RedirectToAction("Operations", new
                {
                    month = bill.BillingMonth,
                    year = bill.BillingYear,
                    btno = bill.Btno
                });
            }

            TempData["ErrorMessage"] = "Bill not found for update";
            return RedirectToAction("Operations");
        }
    }
}