using BMSBT.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadExcel(IFormFile excelFile, int? startRow, int? endRow)
        {
            if (excelFile == null || excelFile.Length == 0)
            {
                TempData["ErrorMessage"] = "Please select an Excel file to upload.";
                return RedirectToAction(nameof(Index));
            }

            if (startRow.HasValue && startRow.Value < 1)
            {
                TempData["ErrorMessage"] = "Start Row must be greater than or equal to 1.";
                return RedirectToAction(nameof(Index));
            }

            if (endRow.HasValue && endRow.Value < 1)
            {
                TempData["ErrorMessage"] = "End Row must be greater than or equal to 1.";
                return RedirectToAction(nameof(Index));
            }

            if (startRow.HasValue && endRow.HasValue && endRow.Value < startRow.Value)
            {
                TempData["ErrorMessage"] = "End Row must be greater than or equal to Start Row.";
                return RedirectToAction(nameof(Index));
            }

            var extension = Path.GetExtension(excelFile.FileName);
            if (!string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Only .xlsx files are supported.";
                return RedirectToAction(nameof(Index));
            }

            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

            using var stream = new MemoryStream();
            await excelFile.CopyToAsync(stream);
            stream.Position = 0;

            using var package = new ExcelPackage(stream);
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();
            if (worksheet == null || worksheet.Dimension == null)
            {
                TempData["ErrorMessage"] = "The uploaded Excel file is empty.";
                return RedirectToAction(nameof(Index));
            }

            int rows = worksheet.Dimension.End.Row;
            int cols = worksheet.Dimension.End.Column;

            var requiredHeaders = new[]
            {
                "btno",
                "department",
                "servicetype",
                "chargename",
                "amount",
                "frequency",
                "month",
                "year"
            };

            var headerMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int headerRow = FindHeaderRow(worksheet, rows, cols, requiredHeaders, headerMap);
            bool usingHeaderMapping = headerMap.Count > 0;

            // Fallback: if no explicit header row found, assume columns A:H in the expected order.
            if (!usingHeaderMapping)
            {
                if (cols < requiredHeaders.Length)
                {
                    TempData["ErrorMessage"] = "Excel must contain at least 8 columns (BT No to Year).";
                    return RedirectToAction(nameof(Index));
                }

                for (int i = 0; i < requiredHeaders.Length; i++)
                    headerMap[requiredHeaders[i]] = i + 1;
            }
            else
            {
                var missing = requiredHeaders.Where(h => !headerMap.ContainsKey(h)).ToList();
                if (missing.Any())
                {
                    TempData["ErrorMessage"] = $"Missing required column(s): {string.Join(", ", missing)}";
                    return RedirectToAction(nameof(Index));
                }
            }

            var inserts = new List<AdditionalCharge>();
            int skipped = 0;
            int autoStartRow = usingHeaderMapping ? headerRow + 1 : 1;
            int dataStartRow = startRow.HasValue ? Math.Max(autoStartRow, startRow.Value) : autoStartRow;
            int dataEndRow = endRow.HasValue ? Math.Min(rows, endRow.Value) : rows;

            if (dataStartRow > rows)
            {
                TempData["ErrorMessage"] = $"Start Row ({dataStartRow}) is beyond available rows ({rows}).";
                return RedirectToAction(nameof(Index));
            }

            if (dataEndRow < dataStartRow)
            {
                TempData["ErrorMessage"] = "No rows available in the selected row range.";
                return RedirectToAction(nameof(Index));
            }

            for (int row = dataStartRow; row <= dataEndRow; row++)
            {
                string btNo = worksheet.Cells[row, headerMap["btno"]].Text?.Trim() ?? string.Empty;
                string department = worksheet.Cells[row, headerMap["department"]].Text?.Trim() ?? string.Empty;
                string serviceType = worksheet.Cells[row, headerMap["servicetype"]].Text?.Trim() ?? string.Empty;
                string chargesName = worksheet.Cells[row, headerMap["chargename"]].Text?.Trim() ?? string.Empty;
                string amountText = worksheet.Cells[row, headerMap["amount"]].Text?.Trim() ?? string.Empty;
                string frequency = worksheet.Cells[row, headerMap["frequency"]].Text?.Trim() ?? string.Empty;
                string month = worksheet.Cells[row, headerMap["month"]].Text?.Trim() ?? string.Empty;
                string year = worksheet.Cells[row, headerMap["year"]].Text?.Trim() ?? string.Empty;

                bool rowIsEmpty = string.IsNullOrWhiteSpace(btNo)
                                  && string.IsNullOrWhiteSpace(department)
                                  && string.IsNullOrWhiteSpace(serviceType)
                                  && string.IsNullOrWhiteSpace(chargesName)
                                  && string.IsNullOrWhiteSpace(amountText)
                                  && string.IsNullOrWhiteSpace(frequency)
                                  && string.IsNullOrWhiteSpace(month)
                                  && string.IsNullOrWhiteSpace(year);
                if (rowIsEmpty)
                {
                    continue;
                }

                // When no header exists, skip title/metadata rows until actual tabular values begin.
                if (!usingHeaderMapping && !LooksLikeDataRow(btNo, amountText, frequency, month, year))
                {
                    continue;
                }

                if (!int.TryParse(amountText, out var amount))
                {
                    skipped++;
                    continue;
                }

                var item = new AdditionalCharge
                {
                    BtNo = btNo,
                    Department = department,
                    ServiceType = serviceType,
                    ChargesName = chargesName,
                    Amount = amount,
                    Frequency = frequency,
                    Month = month,
                    Year = year
                };

                NormalizeModel(item);
                inserts.Add(item);
            }

            if (inserts.Count == 0)
            {
                TempData["ErrorMessage"] = "No valid records found in the uploaded file.";
                return RedirectToAction(nameof(Index));
            }

            await _dbContext.AdditionalCharges.AddRangeAsync(inserts);
            await _dbContext.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Upload complete. Inserted {inserts.Count} record(s) from rows {dataStartRow} to {dataEndRow}." +
                                         (skipped > 0 ? $" Skipped {skipped} row(s)." : string.Empty);

            return RedirectToAction(nameof(Index));
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

        private static string NormalizeHeader(string header)
        {
            return new string(header.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        }

        private static int FindHeaderRow(
            OfficeOpenXml.ExcelWorksheet worksheet,
            int totalRows,
            int totalCols,
            IReadOnlyCollection<string> requiredHeaders,
            Dictionary<string, int> headerMap)
        {
            int scanRows = Math.Min(totalRows, 15);
            for (int row = 1; row <= scanRows; row++)
            {
                var candidateMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (int col = 1; col <= totalCols; col++)
                {
                    var rawHeader = worksheet.Cells[row, col].Text?.Trim();
                    if (string.IsNullOrWhiteSpace(rawHeader))
                        continue;

                    var normalizedHeader = NormalizeHeader(rawHeader);
                    if (requiredHeaders.Contains(normalizedHeader) && !candidateMap.ContainsKey(normalizedHeader))
                    {
                        candidateMap[normalizedHeader] = col;
                    }
                }

                if (candidateMap.Count >= requiredHeaders.Count)
                {
                    foreach (var kvp in candidateMap)
                        headerMap[kvp.Key] = kvp.Value;
                    return row;
                }
            }

            return -1;
        }

        private static bool LooksLikeDataRow(string btNo, string amountText, string frequency, string month, string year)
        {
            if (!int.TryParse(amountText, out _))
                return false;

            bool hasBtNo = !string.IsNullOrWhiteSpace(btNo);
            bool hasMonthYear = !string.IsNullOrWhiteSpace(month) && !string.IsNullOrWhiteSpace(year);
            bool hasFrequency = !string.IsNullOrWhiteSpace(frequency);

            return hasBtNo || hasMonthYear || hasFrequency;
        }

        private bool AdditionalChargeExists(int id)
        {
            return _dbContext.AdditionalCharges.Any(e => e.Uid == id);
        }
    }
}
