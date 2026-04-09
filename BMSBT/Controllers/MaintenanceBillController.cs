using BMSBT.BillServices;
using BMSBT.DTO;
using BMSBT.Models;
using BMSBT.Requests;
using BMSBT.Roles;
using DevExpress.XtraRichEdit.Import.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.Web.CodeGenerators.Mvc.Templates.BlazorIdentity.Pages;
using System.Data.Entity;
using System.Net.Http.Headers;
using System.Security.Policy;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    [CustomAuthorize("Admin,Manager, M-Bill")]
    public class MaintenanceBillController : Controller
    {

        private readonly BmsbtContext _dbContext;
        //private readonly IOperatorService _operatorService;
        private readonly MaintenanceFunctions MaintenanceFunctions;
        //private readonly OperatorDetailsService _operatorDetailsService;
        private readonly ICurrentOperatorService _operatorService;
        private readonly IHttpClientFactory _httpClientFactory;



        public MaintenanceBillController(IHttpClientFactory httpClientFactory, BmsbtContext dbContext, ICurrentOperatorService operatorService)
        {
            _dbContext = dbContext;
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

        public IActionResult CustomerForm()
        {
            return View();
        }


        public IActionResult Index(string project, string sector, string block, int? page)
        {
            if (HttpContext.Session.GetString("UserName") == null)
            {
                return RedirectToAction("Index", "Login");
            }


            // Populate dropdown data
            ViewBag.Projects = _dbContext.Configurations
                                 .Where(c => c.ConfigKey == "Project")
                                 .Select(c => c.ConfigValue)
                                 .ToList();


            var Sectors = _dbContext.Configurations
                                   .Where(c => c.ConfigKey == project)
                                   .Select(c => c.ConfigValue)
                                   .ToList();
            ViewBag.Sectors = Sectors;

            // Get all sectors (assuming the field is "Sector" in your database)
            ViewBag.Blocks = _dbContext.Configurations
                                  .Where(c => c.ConfigKey == "Block" + project)
                                  .Select(c => c.ConfigValue)
                                  .ToList();

            ViewBag.Tarrif = _dbContext.Tarrifs.Select(t => new { t.Uid, t.TarrifName }).ToList();

            // Apply filters
            var query = _dbContext.CustomersDetails.AsQueryable();

            if (!string.IsNullOrEmpty(project))
                query = query.Where(x => x.Project == project);

            if (!string.IsNullOrEmpty(sector))
                query = query.Where(x => x.Sector == sector);

            if (!string.IsNullOrEmpty(block))
                query = query.Where(x => x.Block == block);

            // Total Records Count
            ViewBag.TotalRecords = query.Count();
            // Calculate total records by category
            ViewBag.TotalRecordsByProject = _dbContext.CustomersDetails.Count(x => x.Project == project);
            ViewBag.TotalRecordsBySector = _dbContext.CustomersDetails.Count(x => x.Sector == sector);
            ViewBag.TotalRecordsByBlock = _dbContext.CustomersDetails.Count(x => x.Block == block);




            int pageNumber = page ?? 1;
            int pageSize = 500;

            return View(query.ToPagedList(pageNumber, pageSize));
        }


        // AJAX endpoint for cascading dropdowns
        public JsonResult GetSubprojects(string project)
        {
            if (string.IsNullOrEmpty(project))
                return Json(new List<string>());


            var SubProjects = _dbContext.Configurations
                                 .Where(c => c.ConfigKey == project)
                                 .Select(c => c.ConfigValue)
                                 .ToList();
            return Json(SubProjects);
        }


        public IActionResult MaintenanceCustomerForGeneration(int page = 1, int pageSize = 100, string tariff = null, string project = null, string plotType = null, string plotSize = null)
        {

            var query = _dbContext.CustomersDetails.AsQueryable();


            if (!string.IsNullOrEmpty(tariff))
            {
                query = query.Where(c => c.TariffName == tariff);
            }

            if (!string.IsNullOrEmpty(project))
            {
                query = query.Where(c => c.Project == project);
            }

            if (!string.IsNullOrEmpty(plotType))
            {
                query = query.Where(c => c.PlotType == plotType);
            }

            if (!string.IsNullOrEmpty(plotSize))
            {
                query = query.Where(c => c.Size == plotSize);
            }


            int totalRecords = query.Count();


            var data = query
                .OrderBy(c => c.Uid)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();

            ViewBag.CurrentPage = page;
            ViewBag.PageSize = pageSize;
            ViewBag.TotalRecords = totalRecords;

            return View(data);
        }


        public IActionResult GetCustomersPSPT(string project, string subproject, string tariffname)
        {
            // Check if billstatus is null or empty
            var customers = string.IsNullOrEmpty(tariffname)
                ? _dbContext.CustomersDetails
                          .Where(c => c.Project == project && c.SubProject == subproject)
            .ToList()
                : _dbContext.CustomersDetails
                          .Where(c => c.Project == project && c.SubProject == subproject && c.TariffName == tariffname)
                          .ToList();

            return PartialView("_CustomersGrid", customers);
        }














        [HttpPost]
        public IActionResult EditCustomer(CustomersDetail model)
        {
            if (model == null)
            {
                return BadRequest("Invalid customer data.");
            }

            var existingCustomer = _dbContext.CustomersDetails.FirstOrDefault(c => c.Btno == model.Btno);
            if (existingCustomer == null)
            {
                return NotFound();
            }

            existingCustomer.CustomerName = model.CustomerName;
            existingCustomer.MobileNo = model.MobileNo;
            existingCustomer.TelephoneNo = model.TelephoneNo;
            existingCustomer.BankNo = model.BankNo;
            existingCustomer.City = model.City;
            existingCustomer.SubProject = model.SubProject;
            existingCustomer.Project = model.Project;
            existingCustomer.Size = model.Size;
            existingCustomer.Block = model.Block;
            existingCustomer.Cnicno = model.Cnicno;
            existingCustomer.City = model.City;
            existingCustomer.Project = model.Project;
            existingCustomer.SubProject = model.SubProject;
            existingCustomer.TariffName = model.TariffName;
            existingCustomer.Sector = model.Sector;
            existingCustomer.Block = model.Block;
            existingCustomer.PloNo = model.PloNo;
            existingCustomer.PlotType = model.PlotType;
            existingCustomer.BtnoMaintenance = model.BtnoMaintenance;
            existingCustomer.Category = model.Category;
            existingCustomer.Ntnnumber = model.Ntnnumber;
            existingCustomer.BankNo = model.BankNo;
            existingCustomer.InstalledOn = model.InstalledOn;
            existingCustomer.FatherName = model.FatherName;
            try
            {
                _dbContext.SaveChanges();
                return RedirectToAction("Index"); // Redirect to customer list
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "An error occurred while updating the customer.");
            }

            return View(model);
        }




       
        [HttpGet]
        public IActionResult EditCustomer(string id)
        {
            // Retrieve the customer using Btno
            var customer = _dbContext.CustomersDetails.FirstOrDefault(c => c.Btno == id);
            if (customer == null)
            {
                return NotFound();
            }

            // Retrieve all billing records for the customer (using Btno)
            var bills = _dbContext.MaintenanceBills
                          .Where(b => b.Btno == id)
                          .ToList();

            // Map customer and billing details to the composite view model
            var viewModel = new CustomerBillingViewModel
            {
                Uid = customer.Uid,
                CustomerNo = customer.CustomerNo,
                Btno = customer.Btno,
                CustomerName = customer.CustomerName,
                GeneratedMonthYear = customer.GeneratedMonthYear,
                LocationSeqNo = customer.LocationSeqNo,
                Cnicno = customer.Cnicno,
                FatherName = customer.FatherName,
                InstalledOn = customer.InstalledOn,
                MobileNo = customer.MobileNo,
                TelephoneNo = customer.TelephoneNo,
                MeterType = customer.MeterType,
                Ntnnumber = customer.Ntnnumber,
                City = customer.City,
                Project = customer.Project,
                SubProject = customer.SubProject,
                TariffName = customer.TariffName,
                BankNo = customer.BankNo,
                BtnoMaintenance = customer.BtnoMaintenance,
                Category = customer.Category,
                Block = customer.Block,
                PlotType = customer.PlotType,
                Size = customer.Size,
                Sector = customer.Sector,
                PloNo = customer.PloNo,



                MBills = bills
            };

            return View(viewModel);
        }












        [HttpPost]
        [Route("GenerateMaintenanceBills")]
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











        [HttpGet]
        [Route("Maintenance/MaintenanceBills")]
        public IActionResult MaintenanceBills()
        {
            // Return an empty view (or you could pass an empty IPagedList<BillDTO> if needed)
            return View();
        }

        [HttpPost]
        [Route("Maintenance/MaintenanceBillsPost")]
        public IActionResult MaintenanceBillsPost(string? month, string? year)
        {
            if (string.IsNullOrEmpty(month) || string.IsNullOrEmpty(year))
            {
                ViewBag.ErrorMessage = "Both month and year must be selected.";
                return View("MaintenanceBills"); // Return the view with an error message
            }

            // Query bills joining MaintenanceBills and CustomersDetails
            var bills = (
                from bill in _dbContext.MaintenanceBills
                join customer in _dbContext.CustomersMaintenance
                     on bill.Btno equals customer.BTNo
                where bill.BillingMonth == month && bill.BillingYear == year
                select new BillDTO
                {
                    Uid = bill.Uid,
                    CustomerNo = customer.CustomerNo,
                    Btno = bill.Btno,
                    CustomerName = customer.CustomerName,
                    Cnicno = customer.CNICNo,
                    FatherName = customer.FatherName,
                    InstalledOn = null,
                    MobileNo = customer.MobileNo,
                    TelephoneNo = null,
                    Ntnnumber = null,
                    City = customer.City,
                    Project = customer.Project,
                    SubProject = customer.SubProject,
                    TariffName = "",
                    BankNo = null,
                    BtnoMaintenance = customer.BTNo,
                    Category = customer.Category,
                    Block = customer.Category,
                    PlotType = customer.PlotStatus,
                    Size = customer.Size,
                    Sector = customer.Sector,
                    PloNo = customer.PloNo,
                    BillStatusMaint = customer.BillGenerationStatus,
                    BillStatus = customer.BillGenerationStatus,
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
                    BillAmountAfterDueDate = bill.BillAmountAfterDueDate
                }
            ).ToList();

            if (!bills.Any())
            {
                ViewBag.ErrorMessage = "No bills found for the selected month and year.";
            }

            // Optionally, convert to a paged list (adjust page number and page size as needed)
            var pagedBills = bills.ToPagedList(1, 5000);
            return View("MaintenanceBills", pagedBills); // Pass the view model to the view
        }



        // GET: MaintenanceBills
        public async Task<IActionResult> MaintenanceBillMS()
        {
            return View(await _dbContext.MaintenanceBills.ToListAsync());
        }







        [HttpGet]
        [Route("Maintenance/Details/{id}")]
        public IActionResult Details(string id)
        {
            // InvoiceNo is not a DB column; details route uses uid (see MaintenanceBills / db.txt).
            MaintenanceBill? bill = null;
            if (int.TryParse(id, out var uid))
                bill = _dbContext.MaintenanceBills.FirstOrDefault(b => b.Uid == uid);

            // Handle case where the bill does not exist
            if (bill == null)
            {
                ViewBag.ErrorMessage = "Bill not found.";
                return RedirectToAction("MaintenanceBills");
            }

            // Return the bill to the Details view
            return View(bill);
        }






        [Route("Maintenance/PrintMaintMultiBill")]
        [HttpPost]
     
        public async Task<IActionResult> PrintMaintMultiBill([FromBody] PrintBillRequest request)
        {

            try
            {


                if (string.IsNullOrEmpty(request.uids))
                {
                    return BadRequest("No UIDs provided");
                }

                var client = _httpClientFactory.CreateClient();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/pdf"));

                //var url = $"https://localhost:7050/api/MaintenanceBill/GetMaintenanceBillByUid?uids={request.uids}";

                var url = $"http://172.20.229.3:84/api/MaintenanceBill/GetMaintenanceBillByUid?uids={request.uids}";

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

                return StatusCode((int)response.StatusCode, $"API Error: {errorContent}");
            }
            catch (Exception ex)
            {

                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }


        [Route("Maintenance/MaintTariff")]
        public IActionResult MaintTariff(string project, string category, int? page)
        {
            int pageSize = 20; // Number of records per page
            int pageNumber = (page ?? 1); // If no page is specified, default to the first page

            var query = _dbContext.MaintenanceTarrifs.AsQueryable();

            if (!string.IsNullOrEmpty(project))
            {
                query = query.Where(t => t.Project == project);
            }

            if (!string.IsNullOrEmpty(category))
            {
                query = query.Where(t => t.Category == category);
            }

            // Project list from Configuration (ConfigKey = "Projects", comma-separated ConfigValue)
            var projectsFromConfig = _dbContext.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(p => p)
                .ToList();
            ViewBag.Projects = projectsFromConfig;

            ViewBag.Categories = _dbContext.MaintenanceTarrifs
                .Select(t => t.Category)
                .Where(c => c != null)
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            ViewBag.SelectedProject = project;
            ViewBag.SelectedCategory = category;

            // Convert to paginated list
            var paginatedList = query.ToPagedList(pageNumber, pageSize);

            // Pass the paginated list to the view
            return View(paginatedList);
        }





        [Route("Maintenance/CreateTariff")]
        public IActionResult CreateTariff()
        {
            PopulateCreateTariffDropdowns();
            return View();
        }

        [Route("Maintenance/CreateTariff")]
        [HttpPost]
        public IActionResult CreateTariff(MaintenanceTarrif model)
        {
            PopulateCreateTariffDropdowns();
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            _dbContext.MaintenanceTarrifs.Add(model);
            _dbContext.SaveChanges();
            return RedirectToAction("MaintTariff");
        }

        private void PopulateCreateTariffDropdowns()
        {
            var projectsFromConfig = _dbContext.Configurations
                .Where(c => c.ConfigKey == "Projects" && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(p => p)
                .ToList();

            ViewBag.ProjectList = projectsFromConfig
                .Select(p => new SelectListItem { Value = p, Text = p })
                .ToList();

            ViewBag.CategoryList = new List<SelectListItem>
            {
                new SelectListItem { Value = "Residential", Text = "Residential" },
                new SelectListItem { Value = "Commercial", Text = "Commercial" }
            };
        }

        public IActionResult EditTariff(int id)
        {
            var tariff = _dbContext.MaintenanceTarrifs.Find(id);
            if (tariff == null)
            {
                TempData["ErrorMessage"] = "Tariff not found.";
                return RedirectToAction("MaintTariff");
            }
            return View(tariff);
        }

        // POST: Update Tariff
        [HttpPost]
        public IActionResult EditTariff(MaintenanceTarrif model)
        {
            if (ModelState.IsValid)
            {
                var existingTariff = _dbContext.MaintenanceTarrifs.Find(model.Uid);
                if (existingTariff != null)
                {
                    existingTariff.Project = model.Project;
                    existingTariff.Category = model.Category;
                    existingTariff.Size = model.Size;
                    existingTariff.Charges = model.Charges;
                    existingTariff.Tax = model.Tax;

                    _dbContext.SaveChanges();
                    TempData["SuccessMessage"] = "Tariff updated successfully!";
                    return RedirectToAction("EditTariff");
                }
            }
            TempData["ErrorMessage"] = "Error updating tariff.";
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteTariff(int id)
        {
            var tariff = _dbContext.MaintenanceTarrifs.Find(id);
            if (tariff == null)
            {
                TempData["ErrorMessage"] = "Tariff not found.";
                return RedirectToAction("MaintTariff");
            }

            _dbContext.MaintenanceTarrifs.Remove(tariff);
            _dbContext.SaveChanges();
            TempData["SuccessMessage"] = "Tariff removed successfully.";
            return RedirectToAction("MaintTariff");
        }








        public async Task<IActionResult> PaymentForm()
        {
            string operatorId = HttpContext.Session.GetString("OperatorId");

            if (string.IsNullOrEmpty(operatorId))
            {
                return new JsonResult(new { success = false, message = "Operator ID not found in session" });
            }

           await _operatorService.InitializeAsync(operatorId);
            var currentOperator = _operatorService.GetCurrentOperator();

            var model = new BillViewModel
            {
                BillingMonth = currentOperator.BillingMonth,
                BillingYear = currentOperator.BillingYear

            };

            return View(model);
        }










        [HttpPost]
        public IActionResult OpenBill(BillViewModel model)
        {
            if (string.IsNullOrEmpty(model.BillingMonth) ||
                string.IsNullOrEmpty(model.BillingYear) ||
                string.IsNullOrEmpty(model.Btno))
            {
                ModelState.AddModelError("", "Please provide Billing Month, Billing Year, and Btno.");
                return View("PaymentForm", model);
            }

            // Query the database for a matching bill
            var bill = _dbContext.MaintenanceBills
                .FirstOrDefault(e =>
                    e.BillingMonth == model.BillingMonth &&
                    e.BillingYear == model.BillingYear &&
                     e.PaymentStatus == "Unpaid" &&
                    e.Btno == model.Btno);

            if (bill == null)
            {
                TempData["ErrorMessage"] = "No unpaid bill found for selected month.";
                return View("PaymentForm", model);
            }
            // Update the model with values from the database
            model.ReferenceNumber = bill.CustomerNo;
            model.CustomerName = bill.CustomerName;

            ModelState.Clear();
            return View("PaymentForm", model);
        }









        [HttpPost]
        public IActionResult MarkPaid(BillViewModel model)
        {
            var bill = _dbContext.MaintenanceBills
               .FirstOrDefault(e => e.BillingMonth == model.BillingMonth &&
                                    e.BillingYear == model.BillingYear &&
                                    e.Btno == model.Btno &&
                                    e.PaymentStatus == "Unpaid"

                                    );
            if (bill == null)
            {
                TempData["ErrorMessage"] = "No unpaid bill found for selected month.";
                return View("PaymentForm", model);
            }

            // Update bill payment details
            bill.PaymentStatus = string.IsNullOrEmpty(model.PaymentType) ? "Paid" : model.PaymentType;
            bill.PaymentDate = DateOnly.FromDateTime(DateTime.Now);
            bill.BankDetail = model.BankBranch;

            _dbContext.SaveChanges();

            TempData["SuccessMessage"] = "Bill marked as paid successfully!";
            ModelState.Clear();
            return View("PaymentForm", model);
        }












        [HttpPost]
        public IActionResult PayMaintMultiBill([FromBody] List<string> uids)
        {


            if (uids == null || uids.Count == 0)
            {
                return BadRequest("No bills selected.");
            }

            try
            {
                int processedCount = 0;
                int alreadyPaidCount = 0;
                List<string> alreadyPaidBills = new List<string>();

                foreach (var uid in uids)
                {
                    var bill = _dbContext.MaintenanceBills.FirstOrDefault(b => b.Uid.ToString() == uid);
                    if (bill != null)
                    {
                        if (bill.PaymentStatus == "Paid")
                        {
                            alreadyPaidBills.Add(uid);
                            alreadyPaidCount++; // Increment already paid count
                            continue; // Skip already paid bills
                        }

                        bill.PaymentStatus = "Paid";
                        bill.PaymentDate = DateOnly.FromDateTime(DateTime.Now);
                        processedCount++;
                    }
                }

                _dbContext.SaveChanges();

                return Ok(new
                {
                    message = $"Successfully Paid {processedCount} bills! already Paid Bills Are {alreadyPaidCount} ",
                    processedCount = processedCount,
                    alreadyPaidCount = alreadyPaidCount,
                    processedUids = uids.Except(alreadyPaidBills),
                    alreadyPaidUids = alreadyPaidBills
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, "An error occurred while processing payments.");
            }
        }






        [HttpGet]
        public ActionResult EditMBill(int uid)
        {
            if (uid == null)
            {
                return NotFound();
            }

            // Retrieve the bill from the database using the provided UID
            var bill = _dbContext.MaintenanceBills.FirstOrDefault(b => b.Uid == uid);
            if (bill == null)
            {
                return NotFound("Bill not found");
            }

            // Return the edit view with the bill model
            return View(bill);
        }




        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditMBill(MaintenanceBill model)
        {
            var billToUpdate = _dbContext.MaintenanceBills.FirstOrDefault(b => b.Uid == model.Uid);
            if (billToUpdate == null)
            {
                return NotFound("Bill not found");
            }

            // Update all properties
            billToUpdate.InvoiceNo = model.InvoiceNo;
            billToUpdate.CustomerNo = model.CustomerNo;
            billToUpdate.CustomerName = model.CustomerName;
            billToUpdate.Btno = model.Btno;
            billToUpdate.BillingMonth = model.BillingMonth;
            billToUpdate.BillingYear = model.BillingYear;
            billToUpdate.BillingDate = model.BillingDate;
            billToUpdate.DueDate = model.DueDate;

            billToUpdate.IssueDate = model.IssueDate;
            billToUpdate.ValidDate = model.ValidDate;

            billToUpdate.MeterNo = model.MeterNo;
            billToUpdate.PaymentStatus = model.PaymentStatus;
            billToUpdate.PaymentDate = model.PaymentDate;
            billToUpdate.PaymentMethod = model.PaymentMethod;
            billToUpdate.BankDetail = model.BankDetail;
            billToUpdate.LastUpdated = DateTime.Now;
            billToUpdate.BillAmountInDueDate = model.BillAmountInDueDate;
            billToUpdate.BillSurcharge = model.BillSurcharge;
            billToUpdate.BillAmountAfterDueDate = model.BillAmountAfterDueDate;


            _dbContext.SaveChanges();
            ViewBag.Message = "Bill Update Sucessfully";
            return View(model);
        }

        public class PrintBillRequest
        {
            public string category { get; set; }
            public string uids { get; set; }
            public string project { get; set; }
            public string subProject { get; set; }
            public string sector { get; set; }
            public string block { get; set; }
            public string month { get; set; }
            public string year { get; set; }
        }
    }
}




