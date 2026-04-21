using BMSBT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using X.PagedList;
using X.PagedList.Extensions;

namespace BMSBT.Controllers
{
    public class AdjustmentsController : Controller
    {
        private readonly BmsbtContext _dbContext;

        public AdjustmentsController(BmsbtContext context)
        {
            _dbContext = context;
        }

        public override void OnActionExecuting(Microsoft.AspNetCore.Mvc.Filters.ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        private void PopulateDropdowns()
        {
            ViewBag.BillingTypes = new List<string> { "Maintenance" };
            ViewBag.AdjustmentTypes = new List<string> { "Fixed", "Percentage" };
            ViewBag.Frequencies = new List<string> { "Mothly", "One Time" };
            ViewBag.Months = new List<string>
            {
                "January", "February", "March", "April", "May", "June",
                "July", "August", "September", "October", "November", "December"
            };
            ViewBag.Years = new List<string> { "2025", "2026", "2027" };
        }

        public IActionResult Index(int? page)
        {
            const int pageSize = 20;
            int pageNumber = page ?? 1;

            var items = _dbContext.Adjustments
                .OrderBy(a => a.BtNo)
                .ThenBy(a => a.BillingType)
                .ThenBy(a => a.AdjustmentName)
                .ThenBy(a => a.Year)
                .ThenBy(a => a.Month)
                .ToPagedList(pageNumber, pageSize);

            return View(items);
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.Adjustments
                .FirstOrDefaultAsync(m => m.AdjustmentId == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        public IActionResult Create()
        {
            PopulateDropdowns();
            return View(new Adjustment
            {
                BillingType = "Maintenance"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Adjustment model)
        {
            NormalizeModel(model);
            if (ModelState.IsValid)
            {
                _dbContext.Adjustments.Add(model);
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "Adjustment record created successfully.";
                return RedirectToAction(nameof(Index));
            }

            PopulateDropdowns();
            return View(model);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.Adjustments.FindAsync(id);
            if (item == null)
            {
                return NotFound();
            }

            PopulateDropdowns();
            return View(item);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Adjustment model)
        {
            if (id != model.AdjustmentId)
            {
                return NotFound();
            }

            NormalizeModel(model);
            if (ModelState.IsValid)
            {
                try
                {
                    _dbContext.Update(model);
                    await _dbContext.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Adjustment record updated successfully.";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!AdjustmentExists(model.AdjustmentId))
                    {
                        return NotFound();
                    }
                    throw;
                }
            }

            PopulateDropdowns();
            return View(model);
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var item = await _dbContext.Adjustments
                .FirstOrDefaultAsync(m => m.AdjustmentId == id);

            if (item == null)
            {
                return NotFound();
            }

            return View(item);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var item = await _dbContext.Adjustments.FindAsync(id);
            if (item != null)
            {
                _dbContext.Adjustments.Remove(item);
                await _dbContext.SaveChangesAsync();
                TempData["SuccessMessage"] = "Adjustment record deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private static void NormalizeModel(Adjustment model)
        {
            model.BtNo = string.IsNullOrWhiteSpace(model.BtNo) ? null : model.BtNo.Trim();
            model.BillingType = string.IsNullOrWhiteSpace(model.BillingType) ? null : model.BillingType.Trim();
            model.AdjustmentName = string.IsNullOrWhiteSpace(model.AdjustmentName) ? null : model.AdjustmentName.Trim();
            model.AdjustmentType = string.IsNullOrWhiteSpace(model.AdjustmentType) ? null : model.AdjustmentType.Trim();
            model.Frequency = string.IsNullOrWhiteSpace(model.Frequency) ? null : model.Frequency.Trim();
            model.Month = string.IsNullOrWhiteSpace(model.Month) ? null : model.Month.Trim();
            model.Year = string.IsNullOrWhiteSpace(model.Year) ? null : model.Year.Trim();
        }

        private bool AdjustmentExists(int id)
        {
            return _dbContext.Adjustments.Any(e => e.AdjustmentId == id);
        }
    }
}
