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

        /// <summary>Values from Configuration: optional comma-separated ConfigValue rows for the given key.</summary>
        private List<string> GetConfigList(string configKey)
        {
            var key = configKey.Trim();
            return _dbContext.Configurations
                .AsNoTracking()
                .Where(c => c.ConfigKey != null &&
                            c.ConfigKey.Trim().ToLower() == key.ToLower() &&
                            !string.IsNullOrWhiteSpace(c.ConfigValue))
                .Select(c => c.ConfigValue!)
                .AsEnumerable()
                .SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries))
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(v => v, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private void PopulateDropdowns()
        {
            // Config keys per requirements (table Configuration)
            ViewBag.Departments = GetConfigList("Departments");
            ViewBag.ServiceTypes = GetConfigList("ServiceType");
            ViewBag.ChargesNames = GetConfigList("ChargesName");
            ViewBag.Frequencies = new List<string> { "Monthly", "One Time" };

            ViewBag.Months = new[]
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };

            ViewBag.Years = new[] { "2025", "2026" };
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        public IActionResult Index(int? page)
        {
            int pageSize = 20;
            int pageNumber = page ?? 1;

            var items = _dbContext.AdditionalCharges
                .OrderBy(a => a.BtNo)
                .ThenBy(a => a.Department)
                .ThenBy(a => a.ServiceType)
                .ThenBy(a => a.ChargesName)
                .ThenBy(a => a.Year)
                .ThenBy(a => a.Month)
                .ToPagedList(pageNumber, pageSize);

            return View(items);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
                return NotFound();

            var item = await _dbContext.AdditionalCharges
                .FirstOrDefaultAsync(m => m.Uid == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

        public IActionResult Create()
        {
            PopulateDropdowns();
            return View(new AdditionalCharge());
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AdditionalCharge model)
        {
            NormalizeModel(model);
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

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
                return NotFound();

            var item = await _dbContext.AdditionalCharges.FindAsync(id);
            if (item == null)
                return NotFound();

            PopulateDropdowns();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AdditionalCharge model)
        {
            if (id != model.Uid)
                return NotFound();

            NormalizeModel(model);

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
                        return NotFound();
                    throw;
                }
            }

            PopulateDropdowns();
            return View(model);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
                return NotFound();

            var item = await _dbContext.AdditionalCharges
                .FirstOrDefaultAsync(m => m.Uid == id);

            if (item == null)
                return NotFound();

            return View(item);
        }

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

        private static void NormalizeModel(AdditionalCharge model)
        {
            model.BtNo = string.IsNullOrWhiteSpace(model.BtNo) ? null : model.BtNo.Trim();
            model.Department = string.IsNullOrWhiteSpace(model.Department) ? null : model.Department.Trim();
            model.ServiceType = string.IsNullOrWhiteSpace(model.ServiceType) ? null : model.ServiceType.Trim();
            model.ChargesName = string.IsNullOrWhiteSpace(model.ChargesName) ? null : model.ChargesName.Trim();
            model.Frequency = string.IsNullOrWhiteSpace(model.Frequency) ? null : model.Frequency.Trim();
            model.Month = string.IsNullOrWhiteSpace(model.Month) ? null : model.Month.Trim();
            model.Year = string.IsNullOrWhiteSpace(model.Year) ? null : model.Year.Trim();
        }

        private bool AdditionalChargeExists(int id)
        {
            return _dbContext.AdditionalCharges.Any(e => e.Uid == id);
        }
    }
}
