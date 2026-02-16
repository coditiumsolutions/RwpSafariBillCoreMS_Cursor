using Microsoft.AspNetCore.Mvc;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;

namespace BMSBT.Controllers;

public class ManualRatesController : Controller
{
    private readonly BmsbtContext _context;

    public ManualRatesController(BmsbtContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index(string searchTerm = "")
    {
        if (HttpContext.Session.GetString("UserName") == null)
        {
            return RedirectToAction("Index", "Login");
        }
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");

        var query = _context.ManualRates.AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            query = query.Where(r =>
                (r.CustomerNo != null && r.CustomerNo.Contains(searchTerm)) ||
                (r.Phase != null && r.Phase.Contains(searchTerm)) ||
                (r.Category != null && r.Category.Contains(searchTerm)) ||
                (r.UnitType != null && r.UnitType.Contains(searchTerm)));
        }

        var list = await query.OrderBy(r => r.SNo).ToListAsync();
        return View(list);
    }

    public IActionResult Create()
    {
        if (HttpContext.Session.GetString("UserName") == null)
        {
            return RedirectToAction("Index", "Login");
        }
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ManualRate model)
    {
        if (ModelState.IsValid)
        {
            _context.ManualRates.Add(model);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View(model);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null)
            return NotFound();

        var item = await _context.ManualRates.FindAsync(id);
        if (item == null)
            return NotFound();

        if (HttpContext.Session.GetString("UserName") == null)
            return RedirectToAction("Index", "Login");

        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View(item);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ManualRate model)
    {
        if (id != model.SNo)
            return NotFound();

        if (ModelState.IsValid)
        {
            try
            {
                _context.Attach(model);
                _context.Entry(model).State = EntityState.Modified;
                _context.Entry(model).Property(x => x.Total).IsModified = false;
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ManualRateExists(model.SNo))
                    return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Index));
        }
        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View(model);
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id == null)
            return NotFound();

        var item = await _context.ManualRates.FindAsync(id);
        if (item == null)
            return NotFound();

        if (HttpContext.Session.GetString("UserName") == null)
            return RedirectToAction("Index", "Login");

        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View(item);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null)
            return NotFound();

        var item = await _context.ManualRates.FindAsync(id);
        if (item == null)
            return NotFound();

        if (HttpContext.Session.GetString("UserName") == null)
            return RedirectToAction("Index", "Login");

        ViewBag.UserName = HttpContext.Session.GetString("UserName");
        ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
        return View(item);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var item = await _context.ManualRates.FindAsync(id);
        if (item == null)
            return NotFound();

        _context.ManualRates.Remove(item);
        await _context.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    private bool ManualRateExists(int id)
    {
        return _context.ManualRates.Any(e => e.SNo == id);
    }
}
