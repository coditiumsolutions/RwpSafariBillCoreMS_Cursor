using System.Globalization;
using BMSBT.DTO;
using BMSBT.Models;
using Microsoft.EntityFrameworkCore;
using OfficeOpenXml;

namespace BMSBT.Services;

public interface IMaintenanceBillExcelPaymentService
{
    Task<MaintenanceBillExcelUploadResultDto> ProcessAsync(
        MaintenanceBillExcelUploadRequest request,
        CancellationToken cancellationToken = default);
}

public class MaintenanceBillExcelPaymentService : IMaintenanceBillExcelPaymentService
{
    private readonly BmsbtContext _dbContext;
    private readonly ILogger<MaintenanceBillExcelPaymentService> _logger;

    public MaintenanceBillExcelPaymentService(
        BmsbtContext dbContext,
        ILogger<MaintenanceBillExcelPaymentService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MaintenanceBillExcelUploadResultDto> ProcessAsync(
        MaintenanceBillExcelUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        ExcelPackage.LicenseContext = LicenseContext.NonCommercial;

        await using var stream = new MemoryStream();
        await request.ExcelFile!.CopyToAsync(stream, cancellationToken);
        stream.Position = 0;

        using var package = new ExcelPackage(stream);
        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
        if (worksheet?.Dimension == null)
            throw new InvalidOperationException("Uploaded Excel file does not contain any readable worksheet.");

        var lastRow = worksheet.Dimension.End.Row;
        if (request.StartRowNumber > lastRow)
            throw new InvalidOperationException($"Start row {request.StartRowNumber} is beyond worksheet rows ({lastRow}).");

        var effectiveEndRow = Math.Min(request.EndRowNumber, lastRow);

        var result = new MaintenanceBillExcelUploadResultDto();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            for (var row = request.StartRowNumber; row <= effectiveEndRow; row++)
            {
                result.TotalProcessedRows++;

                try
                {
                    var btno = GetCellString(worksheet.Cells[row, 1].Value);
                    var datePaid = ParseDateOnly(worksheet.Cells[row, 2].Value);
                    var creditAmount = ParseAmountPaid(worksheet.Cells[row, 3].Value);
                    var bankName = GetCellString(worksheet.Cells[row, 4].Value);
                    var paymentStatus = GetCellString(worksheet.Cells[row, 5].Value);
                    var billingMonthRaw = GetCellString(worksheet.Cells[row, 6].Value);
                    var billingYear = GetCellString(worksheet.Cells[row, 7].Value);

                    if (string.IsNullOrWhiteSpace(btno) ||
                        string.IsNullOrWhiteSpace(billingMonthRaw) ||
                        string.IsNullOrWhiteSpace(billingYear))
                    {
                        AddFailure(result, row, btno, billingMonthRaw, billingYear, "Required key columns (BTNo/Month/Year) are missing.");
                        continue;
                    }

                    var normalizedMonth = NormalizeMonth(billingMonthRaw);

                    var bill = await FindMaintenanceBillAsync(btno, normalizedMonth, billingYear, cancellationToken);
                    if (bill == null)
                    {
                        AddFailure(result, row, btno, normalizedMonth, billingYear, "Record not found in MaintenanceBills.");
                        _logger.LogWarning(
                            "PayByExcel missing record. Row={Row}, BTNo={BTNo}, BillingMonth={BillingMonth}, BillingYear={BillingYear}",
                            row,
                            btno,
                            normalizedMonth,
                            billingYear);
                        continue;
                    }

                    bill.PaymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? bill.PaymentStatus : paymentStatus.Trim();
                    bill.BankDetail = bankName;
                    bill.PaymentDate = datePaid;
                    bill.AmountPaid = creditAmount;

                    result.SuccessfullyUpdatedRecords++;
                }
                catch (Exception ex)
                {
                    AddFailure(result, row, string.Empty, string.Empty, string.Empty, ex.Message);
                    _logger.LogWarning(ex, "PayByExcel row processing failed. Row={Row}", row);
                }
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        result.FailedOrNotFoundRecords = result.TotalProcessedRows - result.SuccessfullyUpdatedRecords;
        return result;
    }

    private static void ValidateRequest(MaintenanceBillExcelUploadRequest request)
    {
        if (request.ExcelFile == null || request.ExcelFile.Length == 0)
            throw new ArgumentException("Please upload a valid Excel file.");

        var extension = Path.GetExtension(request.ExcelFile.FileName);
        if (!".xlsx".Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only .xlsx files are supported.");

        if (request.StartRowNumber <= 0 || request.EndRowNumber <= 0)
            throw new ArgumentException("Start and end row numbers must be greater than zero.");

        if (request.StartRowNumber > request.EndRowNumber)
            throw new ArgumentException("Start row number cannot be greater than end row number.");
    }

    private async Task<MaintenanceBill?> FindMaintenanceBillAsync(
        string btno,
        string billingMonth,
        string billingYear,
        CancellationToken cancellationToken)
    {
        var btnoTrimmed = btno.Trim();
        var yearTrimmed = billingYear.Trim();
        var monthLower = billingMonth.Trim().ToLowerInvariant();

        var candidates = await _dbContext.MaintenanceBills
            .Where(x => x.Btno != null &&
                        x.BillingYear != null &&
                        x.BillingMonth != null &&
                        x.Btno.Trim() == btnoTrimmed &&
                        x.BillingYear.Trim() == yearTrimmed)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(x => (x.BillingMonth ?? string.Empty).Trim().ToLowerInvariant() == monthLower);
    }

    private static string GetCellString(object? value)
    {
        return (value?.ToString() ?? string.Empty).Trim();
    }

    private static DateOnly? ParseDateOnly(object? value)
    {
        if (value == null)
            return null;

        if (value is DateTime dt)
            return DateOnly.FromDateTime(dt);

        var raw = value.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var oaDate))
        {
            try
            {
                return DateOnly.FromDateTime(DateTime.FromOADate(oaDate));
            }
            catch
            {
                // Ignore invalid OA date and fall through to textual parsing.
            }
        }

        if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
            return DateOnly.FromDateTime(parsed);

        if (DateTime.TryParse(raw, CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.AllowWhiteSpaces, out parsed))
            return DateOnly.FromDateTime(parsed);

        return null;
    }

    private static int? ParseAmountPaid(object? value)
    {
        if (value == null)
            return null;

        if (value is int i)
            return i;

        if (value is double d)
            return Convert.ToInt32(Math.Round(d, MidpointRounding.AwayFromZero));

        var raw = value.ToString()?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedInt))
            return parsedInt;

        if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedDecimal))
            return Convert.ToInt32(Math.Round(parsedDecimal, MidpointRounding.AwayFromZero));

        return null;
    }

    private static string NormalizeMonth(string month)
    {
        var raw = (month ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(raw))
            return raw;

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var monthNumber) &&
            monthNumber >= 1 &&
            monthNumber <= 12)
        {
            return CultureInfo.GetCultureInfo("en-US").DateTimeFormat.GetMonthName(monthNumber);
        }

        return CultureInfo.GetCultureInfo("en-US")
            .TextInfo
            .ToTitleCase(raw.ToLowerInvariant());
    }

    private static void AddFailure(
        MaintenanceBillExcelUploadResultDto result,
        int rowNumber,
        string btno,
        string billingMonth,
        string billingYear,
        string reason)
    {
        result.MissingRecords.Add(new MaintenanceBillExcelFailureDto
        {
            RowNumber = rowNumber,
            Btno = btno ?? string.Empty,
            BillingMonth = billingMonth ?? string.Empty,
            BillingYear = billingYear ?? string.Empty,
            Reason = reason ?? "Unknown error"
        });
    }
}
