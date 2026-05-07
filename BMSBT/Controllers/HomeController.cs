using BMSBT.Models;
using BMSBT.Requests;
using BMSBT.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly BmsbtContext context;
        private readonly PasswordHasher<User> _passwordHasher;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public HomeController(ILogger<HomeController> logger, BmsbtContext context, IHttpClientFactory httpClientFactory, IConfiguration configuration)
        {
            _logger = logger;
            this.context = context;
            _passwordHasher = new PasswordHasher<User>();
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }



        //[Authorize]
        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            return View();
        }



        //[HttpGet]
        //[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
        //public IActionResult Index()
        //{
        //    return View();
        //}

        public IActionResult Home()
        {
            var data = context.Users.ToList();
            return View(data);
        }

        public IActionResult Users(int? page)
        {
            int pageSize = 10; // Number of records per page
            int pageNumber = page ?? 1; // Default to page 1 if no page is specified

            var data = context.Users.ToList().ToPagedList(pageNumber, pageSize);
            return View(data);
        }


        public IActionResult Customers()
        {
            var data = context.CustomersDetails.ToList();
            return View(data);
        }

        public IActionResult Customer()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var projects = GetConfigurationCsvValuesByKey("Projects");
            var customers = context.CustomersMaintenance
                .OrderByDescending(c => c.Uid)
                .ToList()
                .ToPagedList(1, 20);

            var model = new MaintenanceCustomerFilterViewModel
            {
                Projects = projects,
                Phases = new List<string>(),
                Customers = customers
            };

            return View(model);
        }

        public async Task<IActionResult> Dashboard(string selectedYear, string selectedMonth, string? apiProject, string? phaseNumber)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            if (string.IsNullOrEmpty(selectedYear)) selectedYear = DateTime.Now.Year.ToString();
            if (string.IsNullOrEmpty(selectedMonth)) selectedMonth = DateTime.Now.ToString("MMMM");

            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            var extSection = _configuration.GetSection("ExternalMaintenanceBillsApi");
            var apiBase = extSection["BaseUrl"]?.Trim().TrimEnd('/');
            var pathSeg = extSection["DefaultPathSegment"];
            var proj = string.IsNullOrWhiteSpace(apiProject) ? extSection["DefaultProject"] : apiProject;
            var phase = string.IsNullOrWhiteSpace(phaseNumber) ? extSection["DefaultPhaseNumber"] : phaseNumber;
            ViewBag.ApiProject = proj ?? "";
            ViewBag.PhaseNumber = phase ?? "";

            var projects = GetConfigurationCsvValuesByKey("Projects");
            ViewBag.Projects = projects;

            var phases = !string.IsNullOrWhiteSpace(proj)
                ? GetConfigurationCsvValuesByKey(proj.Trim())
                : new List<string>();
            ViewBag.Phases = phases;

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
                        // External API may lag behind schema changes; use local DB summary fallback.
                        ViewBag.ExternalBillsError = null;
                    }
                    else if (!TryParseMaintenanceBillsJson(body, out var columns, out var rows, out var parseErr))
                    {
                        ViewBag.ExternalBillsError = null;
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
                    ViewBag.ExternalBillsError = null;
                }
            }

            var customerCountsByProject = context.CustomersMaintenance
                .GroupBy(c => c.Project)
                .Select(g => new
                {
                    Project = g.Key,
                    Count = g.Count()
                })
                .OrderBy(g => g.Project)
                .ToList();

            ViewBag.ProjectCustomerCounts = customerCountsByProject;
            ViewBag.TotalCustomerCount = context.CustomersMaintenance.Count();

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
                var local = SummarizeMaintenanceBillsFromLocalDb(selectedMonth, selectedYear, proj, phase);
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
            }

            return View();
        }

        [HttpGet]
        public IActionResult AllBill(string project, string phase, string subProject, string month, string year, string custNoOrName, int? page)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            if (string.IsNullOrWhiteSpace(phase) && !string.IsNullOrWhiteSpace(subProject))
            {
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
                var query = from mb in context.MaintenanceBills
                            join cm in context.CustomersMaintenance on mb.Btno equals cm.BTNo into cmGroup
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBill(int id, string? project, string? phase, string? subProject, string? month, string? year, string? custNoOrName, int? page)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            var bill = context.MaintenanceBills.FirstOrDefault(x => x.Uid == id);
            if (bill == null)
            {
                TempData["AllBillMessage"] = "Bill record was not found.";
                return RedirectToAction(nameof(AllBill), new { project, phase, subProject, month, year, custNoOrName, page });
            }

            if (string.IsNullOrWhiteSpace(bill.Btno))
            {
                TempData["AllBillMessage"] = "BTNo is empty and cannot be marked as deleted.";
                return RedirectToAction(nameof(AllBill), new { project, phase, subProject, month, year, custNoOrName, page });
            }

            var deletedBy = HttpContext.Session.GetString("UserName") ?? "Unknown User";
            var deletedAt = DateTime.Now;
            var deletionHistoryText = $"Bill deleted by {deletedBy} on {deletedAt:dd-MMM-yyyy hh:mm tt}";
            var oldDataPayload = new
            {
                DeleteHistory = deletionHistoryText,
                bill.Uid,
                BTNo = bill.Btno,
                bill.CustomerName,
                bill.BillingMonth,
                bill.BillingYear,
                bill.BillAmountInDueDate,
                ExistingHistory = bill.History
            };

            var auditLog = new AuditLog
            {
                TableName = "MaintenanceBills",
                Operation = "Delete",
                RecordId = bill.Uid.ToString(),
                OldData = JsonSerializer.Serialize(oldDataPayload),
                NewData = null,
                ModuleName = "AllBill",
                ChangedBy = deletedBy,
                ChangedAt = deletedAt,
                IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            };

            context.AuditLogs.Add(auditLog);
            context.MaintenanceBills.Remove(bill);
            context.SaveChanges();

            TempData["AllBillMessage"] = $"Bill BTNo {bill.Btno} deleted permanently.";

            return RedirectToAction(nameof(AllBill), new { project, phase, subProject, month, year, custNoOrName, page });
        }

        [HttpGet]
        public IActionResult GenerateBill(string selectedProject, string selectedPhaseName, string selectedPhaseNumber, string selectedSubProject, string btNoSearch)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            if (string.IsNullOrWhiteSpace(selectedPhaseName) && !string.IsNullOrWhiteSpace(selectedPhaseNumber))
            {
                selectedPhaseName = selectedPhaseNumber;
            }
            if (string.IsNullOrWhiteSpace(selectedPhaseName) && !string.IsNullOrWhiteSpace(selectedSubProject))
            {
                selectedPhaseName = selectedSubProject;
            }

            var userName = HttpContext.Session.GetString("UserName");
            ViewBag.OperatorName = userName;

            if (!string.IsNullOrEmpty(userName))
            {
                var operatorSetup = context.OperatorsSetups
                    .AsEnumerable()
                    .FirstOrDefault(o => string.Equals(o.OperatorName?.Trim(), userName.Trim(), StringComparison.OrdinalIgnoreCase)
                                      || string.Equals(o.OperatorID?.Trim(), userName.Trim(), StringComparison.OrdinalIgnoreCase));

                if (operatorSetup != null)
                {
                    ViewBag.BillingMonth = operatorSetup.BillingMonth;
                    ViewBag.BillingYear = operatorSetup.BillingYear;
                }
            }

            var projects = context.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var phaseNames = new List<string>();
            if (!string.IsNullOrEmpty(selectedProject))
            {
                phaseNames = context.Configurations
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

            var filteredData = new List<MaintSectorCustomersViewModel>();
            if (!string.IsNullOrEmpty(selectedProject))
            {
                var query = context.CustomersMaintenance
                    .Where(c => c.Project != null && c.Project.Trim() == selectedProject.Trim());

                if (!string.IsNullOrEmpty(selectedPhaseName))
                {
                    query = query.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedPhaseName.Trim());
                }

                if (!string.IsNullOrEmpty(btNoSearch))
                {
                    query = query.Where(c => c.BTNo != null && c.BTNo.Contains(btNoSearch));
                }

                var orderedCustomers = query
                    .AsEnumerable()
                    .OrderBy(c => NaturalSortKey(c.PloNo))
                    .ToList();

                filteredData = orderedCustomers.GroupBy(c => c.Category)
                    .Select(g => new MaintSectorCustomersViewModel
                    {
                        Block = g.Key,
                        Customers = g.ToList()
                    })
                    .OrderBy(g => g.Block)
                    .ToList();
            }

            ViewBag.Projects = projects;
            ViewBag.PhaseNames = phaseNames;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedPhaseName = selectedPhaseName;
            ViewBag.BTNoSearch = btNoSearch;

            return View(filteredData);
        }

        [HttpPost]
        public async Task<IActionResult> InitializeCustomers([FromBody] JsonElement request)
        {
            var selectedIds = new List<int>();

            if (request.ValueKind == JsonValueKind.Object)
            {
                if (request.TryGetProperty("selectedIds", out var lowerIds) && lowerIds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in lowerIds.EnumerateArray())
                    {
                        if (id.TryGetInt32(out var parsed))
                            selectedIds.Add(parsed);
                    }
                }
                else if (request.TryGetProperty("SelectedIds", out var upperIds) && upperIds.ValueKind == JsonValueKind.Array)
                {
                    foreach (var id in upperIds.EnumerateArray())
                    {
                        if (id.TryGetInt32(out var parsed))
                            selectedIds.Add(parsed);
                    }
                }
            }

            if (!selectedIds.Any())
            {
                return Json(new { success = false, message = "No customers selected." });
            }

            var customers = context.CustomersMaintenance
                .Where(c => selectedIds.Contains(c.Uid))
                .ToList();

            if (!customers.Any())
            {
                return Json(new { success = false, message = "No matching customers found." });
            }

            foreach (var customer in customers)
            {
                customer.BillGenerationStatus = "Not Generated";
            }

            await context.SaveChangesAsync();

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

        [HttpGet]
        public IActionResult PrintBills()
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            var projects = context.Configurations
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

        [HttpPost]
        public async Task<IActionResult> PrintBills([FromBody] MaintenanceBillController.PrintBillRequest request)
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

                var projectMappings = new Dictionary<string, (string pathSegment, string apiProject, string defaultPhase)>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Safari-1", ("Safari-1", "Safari-1", "Safari Villas") },
                    { "Safari-2", ("Safari-2", "Safari-2", "Safari II") },
                    { "Safari-3", ("Safari-3", "Safari-3", "III-E") },
                    { "Safari 3", ("Safari-3", "Safari-3", "III-E") },
                    { "Bahria Spring", ("BahriaSpring", "bahria spring", "bahria springs") },
                    { "BahriaSpring", ("BahriaSpring", "bahria spring", "bahria springs") },
                    { "Bahria Heights-1", ("SafariHeights", "Bahria Heights-1", "Bahria Heights Ext.1") }
                };

                if (!projectMappings.TryGetValue(selectedProject, out var map))
                {
                    return BadRequest($"Unsupported project '{selectedProject}'.");
                }

                var phaseName = string.IsNullOrWhiteSpace(selectedPhaseName) ? map.defaultPhase : selectedPhaseName;
                var billingMonthValue = string.Equals(selectedProject, "Bahria Spring", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(selectedProject, "BahriaSpring", StringComparison.OrdinalIgnoreCase)
                    ? billingMonth.ToLowerInvariant()
                    : billingMonth;
                var baseUrl = $"http://172.20.229.2:84/api/MaintenanceBills/{Uri.EscapeDataString(map.pathSegment)}";

                var url =
                    $"{baseUrl}?project={Uri.EscapeDataString(map.apiProject)}" +
                    $"&phaseName={Uri.EscapeDataString(phaseName)}" +
                    $"&billingMonth={Uri.EscapeDataString(billingMonthValue)}" +
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

        [HttpGet]
        public IActionResult BillSummary(string? month, string? year, string? project, string? phase)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }
            if (!CurrentUserHasRole("Reports"))
            {
                TempData["AccessDeniedMessage"] = "you do no have rights to open the link";
                return RedirectToAction("AccessDenied", "Login");
            }

            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

            ViewBag.Months = GetMonths();
            ViewBag.Years = GetYears();
            ViewBag.SelectedMonth = month ?? DateTime.Now.ToString("MMMM");
            ViewBag.SelectedYear = year ?? DateTime.Now.Year.ToString();
            ViewBag.SelectedProject = project ?? "";
            ViewBag.SelectedPhase = phase ?? "";

            var selectedMonth = ViewBag.SelectedMonth as string;
            var selectedYear = ViewBag.SelectedYear as string;
            var selectedProject = (ViewBag.SelectedProject as string)?.Trim();
            var selectedPhase = (ViewBag.SelectedPhase as string)?.Trim();

            var projectList = context.CustomersMaintenance
                .Where(c => !string.IsNullOrWhiteSpace(c.Project))
                .Select(c => c.Project!.Trim())
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            ViewBag.Projects = projectList;

            var phaseQuery = context.CustomersMaintenance
                .Where(c => !string.IsNullOrWhiteSpace(c.SubProject));
            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                phaseQuery = phaseQuery.Where(c => c.Project != null && c.Project.Trim() == selectedProject);
            }
            var phaseList = phaseQuery
                .Select(c => c.SubProject!.Trim())
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            ViewBag.Phases = phaseList;

            var customersFiltered = context.CustomersMaintenance.AsQueryable();
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
            if (!string.IsNullOrEmpty(selectedMonth) && !string.IsNullOrEmpty(selectedYear))
            {
                var billsQuery = from mb in context.MaintenanceBills
                                 join cm in context.CustomersMaintenance on mb.Btno equals cm.BTNo
                                 where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                                 where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                                 where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
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

            return View(combined);
        }

        [HttpGet]
        public IActionResult GetBillSummaryData(string month, string year, string? project, string? phase)
        {
            if (!CurrentUserHasRole("Reports"))
                return Json(new List<BillsSummaryCombinedViewModel>());

            var selectedProject = (project ?? "").Trim();
            var selectedPhase = (phase ?? "").Trim();

            var customersFiltered = context.CustomersMaintenance.AsQueryable();
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
            if (!string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
            {
                var billsQuery = from mb in context.MaintenanceBills
                                 join cm in context.CustomersMaintenance on mb.Btno equals cm.BTNo
                                 where mb.BillingMonth == month && mb.BillingYear == year
                                 where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                                 where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
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

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
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

        private static string NaturalSortKey(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var chars = input.ToCharArray();
            var key = new System.Text.StringBuilder(input.Length + 16);
            int i = 0;

            while (i < chars.Length)
            {
                if (char.IsDigit(chars[i]))
                {
                    int start = i;
                    while (i < chars.Length && char.IsDigit(chars[i])) i++;
                    var number = input.Substring(start, i - start);
                    key.Append(number.PadLeft(10, '0'));
                }
                else
                {
                    key.Append(chars[i]);
                    i++;
                }
            }

            return key.ToString();
        }












        private List<string> GetConfigurationCsvValuesByKey(string configKey)
        {
            var rawValues = context.Configurations
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

        private (int totalBills, decimal totalAmountGenerated, int paidCount, decimal paidAmount, int surchargeCount, decimal surchargeAmount, int partialCount, decimal partialAmount, int unpaidCount, decimal unpaidAmount)
            SummarizeMaintenanceBillsFromLocalDb(string selectedMonth, string selectedYear, string? project, string? phase)
        {
            var selectedProject = (project ?? "").Trim();
            var selectedPhase = (phase ?? "").Trim();

            var rows = from mb in context.MaintenanceBills
                       join cm in context.CustomersMaintenance on mb.Btno equals cm.BTNo
                       where mb.BillingMonth == selectedMonth && mb.BillingYear == selectedYear
                       where string.IsNullOrWhiteSpace(selectedProject) || (cm.Project != null && cm.Project.Trim() == selectedProject)
                       where string.IsNullOrWhiteSpace(selectedPhase) || (cm.SubProject != null && cm.SubProject.Trim() == selectedPhase)
                       select new
                       {
                           Status = mb.PaymentStatus,
                           Amount = (decimal?)mb.BillAmountInDueDate ?? 0m
                       };

            var list = rows.ToList();
            int totalBills = list.Count;
            decimal totalAmountGenerated = list.Sum(x => x.Amount);
            int paidCount = 0, surchargeCount = 0, partialCount = 0, unpaidCount = 0;
            decimal paidAmount = 0m, surchargeAmount = 0m, partialAmount = 0m, unpaidAmount = 0m;

            foreach (var row in list)
            {
                var bucket = ClassifyApiPaymentStatus(row.Status);
                switch (bucket)
                {
                    case "paid":
                        paidCount++;
                        paidAmount += row.Amount;
                        break;
                    case "surcharge":
                        surchargeCount++;
                        surchargeAmount += row.Amount;
                        break;
                    case "partial":
                        partialCount++;
                        partialAmount += row.Amount;
                        break;
                    default:
                        unpaidCount++;
                        unpaidAmount += row.Amount;
                        break;
                }
            }

            return (totalBills, totalAmountGenerated, paidCount, paidAmount, surchargeCount, surchargeAmount, partialCount, partialAmount, unpaidCount, unpaidAmount);
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


        [HttpGet]
        public IActionResult CreateUser()
        {
            return View();
        }




        [HttpPost]
        public IActionResult CreateUser(User user, List<string> Role)
        {
            if (Role != null && Role.Count > 0)
            {
                user.Role = string.Join(",", Role); // Store roles as comma-separated string
            }

            // Hash the password before saving
            user.PasswordHash = _passwordHasher.HashPassword(user, user.PasswordHash);

            context.Users.Add(user);
            context.SaveChanges();

            return RedirectToAction("Users");
        }


        [HttpGet]
        public IActionResult EditUser(int id)
        {
            var user = context.Users.Find(id);
            if (user == null)
            {
                return NotFound();
            }

            // If Role is not null, convert it into a list for multi-selection
            ViewBag.SelectedRoles = user.Role?.Split(',') ?? new string[] { };

            return View(user);
        }

        [HttpPost]

        public IActionResult EditUser(User user, string[] Role, string? newPassword)
        {
            var existingUser = context.Users.FirstOrDefault(u => u.Uid == user.Uid);
            if (existingUser == null)
            {
                return NotFound();
            }

            existingUser.EmployeeId = user.EmployeeId;
            existingUser.Username = user.Username;
            existingUser.Role = Role != null ? string.Join(",", Role) : null;

            // Hash new password only if provided
            if (!string.IsNullOrEmpty(user.PasswordHash))
            {
                existingUser.PasswordHash = _passwordHasher.HashPassword(existingUser, user.PasswordHash);
            }

            context.SaveChanges();
            return RedirectToAction("Users");
        }






    }
}
