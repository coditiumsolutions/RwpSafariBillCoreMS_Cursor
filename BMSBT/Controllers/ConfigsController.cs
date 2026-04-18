using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace BMSBT.Controllers
{
    public class ConfigsController : Controller
    {
        public readonly BmsbtContext _context;
        public ConfigsController(BmsbtContext context)
        {
            _context = context;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            ViewBag.UserName = HttpContext.Session.GetString("UserName");
            ViewBag.LoginTime = HttpContext.Session.GetString("LoginTime");
            base.OnActionExecuting(context);
        }

        public IActionResult Index()
        {
            return View();
        }

        // GET: Configs/AllConfigs
        public async Task<IActionResult> AllConfigs(string searchTerm = "", int page = 1, int pageSize = 10)
        {
            var query = _context.Configurations.AsQueryable();

            // Apply search filter (for server-side search if needed)
            if (!string.IsNullOrWhiteSpace(searchTerm))
            {
                query = query.Where(c => 
                    (c.ConfigKey != null && c.ConfigKey.Contains(searchTerm)) ||
                    (c.ConfigValue != null && c.ConfigValue.Contains(searchTerm))
                );
            }

            // Get all configurations for DataTables grid (client-side pagination)
            var allConfigurations = await query
                .OrderBy(c => c.Uid)
                .ToListAsync();

            var totalRecords = allConfigurations.Count;
            var totalPages = totalRecords > 0 ? (int)Math.Ceiling(totalRecords / (double)pageSize) : 0;

            var viewModel = new ConfigurationListViewModel
            {
                Configurations = allConfigurations, // Return all records for DataTables
                CurrentPage = totalPages > 0 ? page : 1,
                TotalPages = totalPages,
                SearchTerm = searchTerm,
                TotalRecords = totalRecords,
                PageSize = pageSize
            };

            return View(viewModel);
        }









        // Get the city-related ConfigValues from Configuration
        public IActionResult GetProjects()
        {
            var projects = _context.Configurations
                                 .Where(c => c.ConfigKey == "Lahore")
                                 .Select(c => c.ConfigValue)
                                 .ToList();
            return Json(projects); // Return JSON for dropdown
        }


        public IActionResult GetSectors(string project) 
        {
            var sectors = _context.Configurations
                                  .Where(c => c.ConfigKey == "Sector" + project)
                                  .Select(c => c.ConfigValue)
                                  .ToList();
            return Json(sectors); // Return JSON for dropdown
        }


        // Get the city-related ConfigValues from Configuration
        public IActionResult GetKeyValues()
        {
            var projects = _context.Configurations
                                 .Where(c => c.ConfigKey == "Key")
                                 .Select(c => c.ConfigValue)
                                 .ToList();
            return Json(projects); // Return JSON for dropdown
        }



        // Get the city-related ConfigValues from Configuration
        public IActionResult GetTariffs()
        {
            var tariffs = _context.Tarrifs.Select(t => new { t.Uid, t.TarrifName }).ToList();

            if (tariffs == null || !tariffs.Any())
            {
                Console.WriteLine("No tariffs found in the database.");
            }
            else
            {
                Console.WriteLine("Tariffs found: " + tariffs.Count);
            }

            return Json(tariffs); // Return JSON for dropdown
        }



        // Get the city-related ConfigValues from Configuration
        public IActionResult GetBillStatus()
        {
            var billstatus = _context.Configurations
                                 .Where(c => c.ConfigKey == "BillStatus")
                                 .Select(c => c.ConfigValue)
                                 .ToList();
            return Json(billstatus); // Return JSON for dropdown
        }

        // Get the city-related ConfigValues from Configuration
        public IActionResult GetSubProjects(string project)
        {
            var projects = _context.Configurations
                                 .Where(c => c.ConfigKey == project)
                                 .Select(c => c.ConfigValue)
                                 .ToList();
            return Json(projects); // Return JSON for dropdown
        }



        public IActionResult ConfigKeyValues(string project)
        {
            if (project == "All")
            {
                var db = _context.Configurations.ToList();
                return PartialView("_KeyValueGridView", db);
            }



            var customers = _context.Configurations
                                    .Where(c => c.ConfigKey == project)
                                    .ToList();
            return PartialView("_KeyValueGridView", customers);
        }



        // GET: Configs/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Configs/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ConfigId,ConfigKey,ConfigValue")] Configuration configuration)
        {
            if (ModelState.IsValid)
            {
                _context.Add(configuration);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(AllConfigs));
            }
            return View(configuration);
        }


        // GET: Configs/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var configuration = await _context.Configurations.FindAsync(id);
            if (configuration == null)
            {
                return NotFound();
            }
            return View(configuration);
        }



        // POST: Configs/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Uid,ConfigId,ConfigKey,ConfigValue")] Configuration configuration)
        {
            if (id != configuration.Uid)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(configuration);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ConfigurationExists(configuration.Uid))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(AllConfigs));
            }
            return View(configuration);
        }

        // GET: Configs/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var configuration = await _context.Configurations.FirstOrDefaultAsync(m => m.Uid == id);
            if (configuration == null)
            {
                return NotFound();
            }

            return View(configuration);
        }




        // GET: Configs/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var configuration = await _context.Configurations.FirstOrDefaultAsync(m => m.Uid == id);
            if (configuration == null)
            {
                return NotFound();
            }

            return View(configuration);
        }

        // POST: Configs/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var configuration = await _context.Configurations.FindAsync(id);
            if (configuration == null)
            {
                return NotFound();
            }
            _context.Configurations.Remove(configuration);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(AllConfigs));
        }

        private bool ConfigurationExists(int id)
        {
            return _context.Configurations.Any(e => e.Uid == id);
        }










    }
}