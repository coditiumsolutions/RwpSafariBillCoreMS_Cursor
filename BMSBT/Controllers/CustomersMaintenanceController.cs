using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using BMSBT.Models;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BMSBT.Controllers
{
    public class CustomersMaintenanceController : Controller
    {
        private readonly BmsbtContext _context; // Replace with your actual DbContext

        public CustomersMaintenanceController(BmsbtContext context)
        {
            _context = context;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        // GET: CustomersMaintenance
        public async Task<IActionResult> Index()
        {
            var customers = await _context.CustomersMaintenance.ToListAsync();
            return View(customers);
        }

        // GET: CustomersMaintenance/SearchCustomers
        public async Task<IActionResult> SearchCustomers()
        {
            var customers = await _context.CustomersMaintenance.ToListAsync();
            return View(customers);
        }


        // GET: CustomersMaintenance/GetCustomersPSPS
        public async Task<IActionResult> GetCustomersPSPS(string project, string subproject, string sectorname)
        {
            var query = _context.CustomersMaintenance.AsQueryable();

            if (!string.IsNullOrEmpty(project))
            {
                query = query.Where(c => c.Project == project);
            }

            if (!string.IsNullOrEmpty(subproject))
            {
                query = query.Where(c => c.SubProject == subproject);
            }

            if (!string.IsNullOrEmpty(sectorname))
            {
                query = query.Where(c => c.Sector == sectorname);
            }

            var customers = await query.ToListAsync();

            // Return HTML string for table rows
            var htmlContent = new System.Text.StringBuilder();

            foreach (var item in customers)
            {
                htmlContent.AppendLine("<tr data-customer-id='" + item.Uid + "'>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.CustomerName) ? "" : System.Net.WebUtility.HtmlEncode(item.CustomerName)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.BTNo) ? "" : System.Net.WebUtility.HtmlEncode(item.BTNo)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.CNICNo) ? "" : System.Net.WebUtility.HtmlEncode(item.CNICNo)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.FatherName) ? "" : System.Net.WebUtility.HtmlEncode(item.FatherName)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.MobileNo) ? "" : System.Net.WebUtility.HtmlEncode(item.MobileNo)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.Project) ? "" : System.Net.WebUtility.HtmlEncode(item.Project)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.SubProject) ? "" : System.Net.WebUtility.HtmlEncode(item.SubProject)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.Sector) ? "" : System.Net.WebUtility.HtmlEncode(item.Sector)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.Category) ? "" : System.Net.WebUtility.HtmlEncode(item.Category)) + "</td>");
                htmlContent.AppendLine("<td>" + (string.IsNullOrEmpty(item.StreetNumber) ? "" : System.Net.WebUtility.HtmlEncode(item.StreetNumber)) + "</td>");
                htmlContent.AppendLine("<td><span class='badge " + GetStatusBadgeClass(item.BillGenerationStatus) + "'>" + (string.IsNullOrEmpty(item.BillGenerationStatus) ? "" : System.Net.WebUtility.HtmlEncode(item.BillGenerationStatus)) + "</span></td>");
                htmlContent.AppendLine("<td>");
                htmlContent.AppendLine("<div class='btn-group btn-group-sm'>");
                htmlContent.AppendLine("<a href='/CustomersMaintenance/Edit/" + item.Uid + "' class='btn btn-outline-primary' title='Edit'><i class='fas fa-edit'></i></a>");
                htmlContent.AppendLine("<a href='/CustomersMaintenance/Details/" + item.Uid + "' class='btn btn-outline-info' title='Details'><i class='fas fa-info-circle'></i></a>");
                htmlContent.AppendLine("<a href='/CustomersMaintenance/Delete/" + item.Uid + "' class='btn btn-outline-danger' title='Delete'><i class='fas fa-trash'></i></a>");
                htmlContent.AppendLine("</div>");
                htmlContent.AppendLine("</td>");
                htmlContent.AppendLine("</tr>");
            }

            if (!customers.Any())
            {
                htmlContent.AppendLine("<tr><td colspan='12' class='text-center'>No maintenance customers found for the selected criteria</td></tr>");
            }

            return Content(htmlContent.ToString(), "text/html");
        }

        private string GetStatusBadgeClass(string status)
        {
            if (string.IsNullOrEmpty(status)) return "badge-secondary";

            return status.ToLower() switch
            {
                "paid" => "badge-paid",
                "pending" => "badge-pending",
                "overdue" => "badge-overdue",
                _ => "badge-secondary"
            };
        }



        // GET: CustomersMaintenance/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.CustomersMaintenance
                .FirstOrDefaultAsync(m => m.Uid == id);
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // GET: CustomersMaintenance/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: CustomersMaintenance/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CustomersMaintenance customer)
        {
            customer.Sector = string.IsNullOrWhiteSpace(customer.Sector) ? string.Empty : customer.Sector.Trim();

            if (ModelState.IsValid)
            {
                _context.Add(customer);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(customer);
        }

        // GET: CustomersMaintenance/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var customer = await _context.CustomersMaintenance.FindAsync(id);
            if (customer == null) return NotFound();

            PopulateEditDropdowns(customer.Project);
            return View(customer);
        }

        // POST: CustomersMaintenance/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CustomersMaintenance customer)
        {
            if (id != customer.Uid) return NotFound();

            customer.Sector = string.IsNullOrWhiteSpace(customer.Sector) ? string.Empty : customer.Sector.Trim();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(customer);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"Customer '{customer.CustomerName}' updated successfully.";
                    return RedirectToAction("Customer", "Home");
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CustomerExists(customer.Uid)) return NotFound();
                    throw;
                }
            }
            PopulateEditDropdowns(customer.Project);
            return View(customer);
        }

        [HttpGet]
        public JsonResult GetPhasesByProject(string project)
        {
            if (string.IsNullOrWhiteSpace(project))
                return Json(new List<string>());

            var phases = _context.Configurations
                .Where(c => c.ConfigKey != null && c.ConfigKey.Trim() == project.Trim()
                            && !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .OrderBy(v => v)
                .ToList();
            return Json(phases);
        }

        private void PopulateEditDropdowns(string? selectedProject)
        {
            ViewBag.ProjectList = _context.Configurations
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

            var phaseNumbers = new List<SelectListItem>();
            if (!string.IsNullOrWhiteSpace(selectedProject))
            {
                phaseNumbers = _context.Configurations
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

        // GET: CustomersMaintenance/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var customer = await _context.CustomersMaintenance
                .FirstOrDefaultAsync(m => m.Uid == id);
            if (customer == null)
            {
                return NotFound();
            }

            return View(customer);
        }

        // POST: CustomersMaintenance/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var customer = await _context.CustomersMaintenance.FindAsync(id);
            if (customer != null)
            {
                _context.CustomersMaintenance.Remove(customer);
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // POST: CustomersMaintenance/ProcessCustomers
        [HttpPost]
        public async Task<IActionResult> ProcessCustomers([FromBody] List<CustomerSelectionModel> selectedCustomers)
        {
            try
            {
                // Your processing logic here
                foreach (var customer in selectedCustomers)
                {
                    var cust = await _context.CustomersMaintenance.FindAsync(customer.Uid);
                    if (cust != null)
                    {
                        cust.BillGenerationStatus = "Processed";
                        // Add other processing logic
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Customers processed successfully!" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error processing customers: " + ex.Message });
            }
        }

        private bool CustomerExists(int id)
        {
            return _context.CustomersMaintenance.Any(e => e.Uid == id);
        }

        
    }

    // Model for selected customers
    public class CustomerSelectionModel
    {
        public int Uid { get; set; }
        public string Btno { get; set; }
        public string CustomerName { get; set; }
        public string TariffName { get; set; }
    }
}