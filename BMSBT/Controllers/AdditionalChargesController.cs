using BMSBT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using X.PagedList;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    public class AdditionalChargesController : Controller
    {
        private readonly BmsbtContext _dbContext;

        public AdditionalChargesController(BmsbtContext context)
        {
            _dbContext = context;
        }

        private void PopulateDropdowns()
        {
            // Service Type values from Configuration where ConfigKey = 'ServiceType'
            ViewBag.ServiceTypes = _dbContext.Configurations
                .Where(c => c.ConfigKey == "ServiceType" && c.ConfigValue != null)
                .Select(c => c.ConfigValue!)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            // Service Name values from Configuration where ConfigKey = 'ServiceName'
            ViewBag.ServiceNames = _dbContext.Configurations
                .Where(c => c.ConfigKey == "ServiceName" && c.ConfigValue != null)
                .Select(c => c.ConfigValue!)
                .Distinct()
                .OrderBy(v => v)
                .ToList();

            // Month names
            ViewBag.Months = new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };

            // Years: 2025, 2026
            ViewBag.Years = new[] { "2025", "2026" };
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        // GET: AdditionalCharges
        public IActionResult Index(int? page)
        {
            int pageSize = 20;
            int pageNumber = page ?? 1;

            var items = _dbContext.AdditionalCharges
                .OrderBy(a => a.CustomerNo)
                .ThenBy(a => a.ServiceType)
                .ThenBy(a => a.ServiceName)
                .ThenBy(a => a.Year)
                .ThenBy(a => a.Month)
                .ToPagedList(pageNumber, pageSize);

            return View(items);
        }

        // GET: AdditionalCharges/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.AdditionalCharges
                .FirstOrDefaultAsync(m => m.Uid == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        // GET: AdditionalCharges/Create
        public IActionResult Create()
        {
            PopulateDropdowns();
            return View(new AdditionalCharge());
        }

        // POST: AdditionalCharges/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AdditionalCharge model)
        {
            if (ModelState.IsValid)
            {
                _dbContext.AdditionalCharges.Add(model);
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "Additional charges record created successfully.";
                return RedirectToAction(nameof(Index));
            }
            PopulateDropdowns();
            return View(model);
        }

        // GET: AdditionalCharges/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.AdditionalCharges.FindAsync(id);
            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        // POST: AdditionalCharges/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AdditionalCharge model)
        {
            if (id != model.Uid)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _dbContext.Update(model);
                    await _dbContext.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Additional charges record updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AdditionalChargeExists(model.Uid))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            return View(model);
        }

        // GET: AdditionalCharges/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.AdditionalCharges
                .FirstOrDefaultAsync(m => m.Uid == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        // POST: AdditionalCharges/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _dbContext.AdditionalCharges.FindAsync(id);
            if (item != null)
            {
                _dbContext.AdditionalCharges.Remove(item);
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "Additional charges record deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool AdditionalChargeExists(int id)
        {
            return _dbContext.AdditionalCharges.Any(e => e.Uid == id);
        }
    }
}

