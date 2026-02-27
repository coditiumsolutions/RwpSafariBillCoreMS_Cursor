using BMSBT.BillServices;
using BMSBT.Models;
using BMSBT.Requests;
using BMSBT.ViewModels;
using BMSBT.DTO;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Data.Entity;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
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

       
        public MaintenanceNewController(IHttpClientFactory httpClientFactory, BmsbtContext context, ICurrentOperatorService operatorService)
        {
            _dbContext = context;
            MaintenanceFunctions = new MaintenanceFunctions(_dbContext);
            _operatorService = operatorService;
            _httpClientFactory = httpClientFactory;
        
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }






        public IActionResult Index(string selectedYear, string selectedMonth)
        {
            // Defaults to current month/year if none provided
            if (string.IsNullOrEmpty(selectedYear)) selectedYear = DateTime.Now.Year.ToString();
            if (string.IsNullOrEmpty(selectedMonth)) selectedMonth = DateTime.Now.ToString("MMMM");

            // Retain the selected values
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

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


            // Maintenance Bills summary (Generated, Paid, Unpaid) based on selected month/year
            var billsQuery = _dbContext.MaintenanceBills.AsQueryable();

            if (!string.IsNullOrEmpty(selectedYear))
            {
                billsQuery = billsQuery.Where(b => b.BillingYear == selectedYear);
            }

            if (!string.IsNullOrEmpty(selectedMonth))
            {
                billsQuery = billsQuery.Where(b => b.BillingMonth == selectedMonth);
            }

            int totalBills = billsQuery.Count();
            decimal totalAmountGenerated = billsQuery.Sum(b => (decimal?)b.BillAmountInDueDate) ?? 0;

            // Individual Status Calculations
            var paidBills = billsQuery.Where(b => b.PaymentStatus == "paid" || b.PaymentStatus == "Paid").ToList();
            var surchargeBills = billsQuery.Where(b => 
                b.PaymentStatus == "paid with surcharge" || 
                b.PaymentStatus == "Paid with Surcharge" || 
                b.PaymentStatus == "PaidWithSurcharge" ||
                b.PaymentStatus == "Paid with surcharge").ToList();
            var partialBills = billsQuery.Where(b => b.PaymentStatus == "paritally paid" || b.PaymentStatus == "Partially Paid" || b.PaymentStatus == "partially paid").ToList();
            var unpaidBills = billsQuery.Where(b => b.PaymentStatus == "unpaid" || b.PaymentStatus == "Unpaid" || string.IsNullOrEmpty(b.PaymentStatus)).ToList();

            ViewBag.TotalBills = totalBills;
            ViewBag.TotalAmountGenerated = totalAmountGenerated;

            ViewBag.PaidCount = paidBills.Count;
            ViewBag.PaidAmount = paidBills.Sum(b => b.BillAmountInDueDate) ?? 0m;

            ViewBag.SurchargeCount = surchargeBills.Count;
            ViewBag.SurchargeAmount = surchargeBills.Sum(b => b.BillAmountInDueDate) ?? 0m;

            ViewBag.PartialCount = partialBills.Count;
            ViewBag.PartialAmount = partialBills.Sum(b => b.BillAmountInDueDate) ?? 0m;

            ViewBag.UnpaidBillsCount = unpaidBills.Count;
            ViewBag.BillUnpaidAmount = unpaidBills.Sum(b => b.BillAmountInDueDate) ?? 0m;

            return View();
        }


        public IActionResult CustomersMaintenance()
        {
            var projects = _dbContext.CustomersMaintenance
                .Select(c => c.Project.Trim())
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            var model = new MaintenanceCustomerFilterViewModel
            {
                Projects = projects,
                Blocks = new List<string>(),
                Customers = new List<CustomersMaintenance>().ToPagedList(1, 20)
            };

            return View(model);
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
                .Select(c => c.Block)
                .Distinct()
                .OrderBy(b => b)
                .ToList();

            return Json(blocks);
        }

        [HttpGet]
        public PartialViewResult FilterCustomers(string project, string block, string btNo, int? page)
        {
            var query = _dbContext.CustomersMaintenance.AsQueryable();

            if (!string.IsNullOrWhiteSpace(project))
            {
                query = query.Where(c => c.Project == project);
            }

            if (!string.IsNullOrWhiteSpace(block))
            {
                query = query.Where(c => c.Block == block);
            }

            if (!string.IsNullOrWhiteSpace(btNo))
            {
                query = query.Where(c => c.BTNo != null && c.BTNo.Contains(btNo));
            }

            int pageSize = 20;
            int pageNumber = (page ?? 1);

            var customers = query
                .OrderBy(c => c.Project)
                .ThenBy(c => c.Block)
                .ThenBy(c => c.BTNo)
                .ToPagedList(pageNumber, pageSize);

            return PartialView("_MaintenanceCustomersGrid", customers);
        }



        public async Task<IActionResult> GenerateBill(string selectedProject, string selectedSubProject, string btNoSearch)
        {
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

            // Dropdown projects
            var projects = _dbContext.CustomersMaintenance
                .Where(c => c.Project != null)
                .Select(c => c.Project!.Trim())
                .Distinct()
                .ToList();

            // SubProjects for selected project
            var subProjects = new List<string>();
            if (!string.IsNullOrEmpty(selectedProject))
            {
                subProjects = _dbContext.CustomersMaintenance
                    .Where(c => c.Project != null && c.Project.Trim() == selectedProject.Trim() && c.SubProject != null)
                    .Select(c => c.SubProject!.Trim())
                    .Distinct()
                    .ToList();
            }

            // Start with empty result
            var filteredData = new List<MaintSectorCustomersViewModel>();

            // Only load if project is selected
            if (!string.IsNullOrEmpty(selectedProject))
            {
                var query = _dbContext.CustomersMaintenance
                    .Where(c => c.Project != null && c.Project.Trim() == selectedProject.Trim());

                if (!string.IsNullOrEmpty(selectedSubProject))
                {
                    query = query.Where(c => c.SubProject != null && c.SubProject.Trim() == selectedSubProject.Trim());
                }

                if (!string.IsNullOrEmpty(btNoSearch))
                {
                    query = query.Where(c => c.BTNo != null && c.BTNo.Contains(btNoSearch));
                }

                filteredData = query.GroupBy(c => c.Block)
                .Select(g => new MaintSectorCustomersViewModel
                {
                    Block = g.Key,
                    Customers = g.ToList()
                 })
                .OrderBy(g => g.Block)
                .ToList();

            }

            ViewBag.Projects = projects;
            ViewBag.SubProjects = subProjects;
            ViewBag.SelectedProject = selectedProject;
            ViewBag.SelectedSubProject = selectedSubProject;

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
                                 join cm in _dbContext.CustomersMaintenance on mb.CustomerNo equals cm.CustomerNo
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
                                 join cm in _dbContext.CustomersMaintenance on mb.CustomerNo equals cm.CustomerNo
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
                .Select(c => c.Block)
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
                baseQuery = baseQuery.Where(x => x.cm.Block == block);
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
                Block = x.cm.Block,
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
                .Select(x => x.Block)
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
        /// All Bill: same three dropdowns as PrintMMultiBills (Project, Billing Month, Billing Year). Displays records in grid with pagination; optional CustNo/Name search; double-click opens Details.
        /// </summary>
        [HttpGet]
        public IActionResult AllBills(string project, string month, string year, string custNoOrName, int? page)
        {
            var projects = _dbContext.CustomersMaintenance
                .Select(c => c.Project.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            if (projects == null || !projects.Any())
            {
                projects = _dbContext.Configurations
                    ?.Where(c => c.ConfigKey == "Project")
                    ?.Select(c => c.ConfigValue)
                    ?.ToList() ?? new List<string>();
            }
            ViewBag.Projects = projects ?? new List<string>();
            ViewBag.SelectedProject = project ?? "";
            ViewBag.SelectedMonth = month ?? "";
            ViewBag.SelectedYear = year ?? "";
            ViewBag.CustNoOrName = custNoOrName ?? "";

            const int pageSize = 20;
            var pageNumber = page ?? 1;
            if (pageNumber < 1) pageNumber = 1;

            var list = new List<MaintenanceBillViewModel>();
            var totalRecords = 0;

            if (!string.IsNullOrEmpty(project) && !string.IsNullOrEmpty(month) && !string.IsNullOrEmpty(year))
            {
                var term = custNoOrName?.Trim();
                var query = from mb in _dbContext.MaintenanceBills
                            join cm in _dbContext.CustomersMaintenance on mb.CustomerNo equals cm.CustomerNo
                            where cm.Project == project && mb.BillingMonth == month && mb.BillingYear == year
                                  && (string.IsNullOrEmpty(term)
                                      || (mb.CustomerNo != null && mb.CustomerNo.Contains(term))
                                      || (mb.CustomerName != null && mb.CustomerName.Contains(term)))
                            select new MaintenanceBillViewModel
                            {
                                Uid = mb.Uid,
                                CustomerNo = mb.CustomerNo,
                                InvoiceNo = mb.InvoiceNo,
                                CustomerName = mb.CustomerName,
                                Btno = mb.Btno,
                                BillingMonth = mb.BillingMonth,
                                BillingYear = mb.BillingYear,
                                BillAmountInDueDate = mb.BillAmountInDueDate,
                                BillAmountAfterDueDate = mb.BillAmountAfterDueDate,
                                PaymentStatus = mb.PaymentStatus,
                                Block = cm.Block,
                                DueDate = mb.DueDate
                            };

                totalRecords = query.Count();
                list = query.OrderBy(x => x.CustomerNo)
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
            var projects = _dbContext.CustomersMaintenance
                .Select(c => c.Project.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            if (projects == null || !projects.Any())
            {
                projects = _dbContext.Configurations
                    ?.Where(c => c.ConfigKey == "Project")
                    ?.Select(c => c.ConfigValue)
                    ?.ToList() ?? new List<string>();
            }

            ViewBag.Projects = projects ?? new List<string>();

            var subProjects = _dbContext.CustomersMaintenance
                .Where(c => c.SubProject != null)
                .Select(c => c.SubProject!.Trim())
                .Where(sp => !string.IsNullOrEmpty(sp))
                .Distinct()
                .OrderBy(sp => sp)
                .ToList();
            ViewBag.SubProjects = subProjects ?? new List<string>();

            return View();
        }

        [Route("PrintMMultiBills")]
        [HttpPost]
        public async Task<IActionResult> PrintMMultiBills([FromBody] PrintBillRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.project) ||
                    string.IsNullOrEmpty(request.month) ||
                    string.IsNullOrEmpty(request.year))
                {
                    return BadRequest("Project, Billing Month and Billing Year are required.");
                }

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                var url = $"http://172.20.228.2:82/api/maintenancebills?project={Uri.EscapeDataString(request.project)}&billingMonth={Uri.EscapeDataString(request.month)}&billingYear={Uri.EscapeDataString(request.year)}";
                if (!string.IsNullOrEmpty(request.subProject))
                    url += $"&subProject={Uri.EscapeDataString(request.subProject)}";

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
                            BtnoMaintenance = customer != null ? customer.BTNoMaintenance : "",
                            Category = customer != null ? customer.Category : "",
                            Block = customer != null ? customer.Block : "",
                            PlotType = customer != null ? customer.PlotType : "",
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