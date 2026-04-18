using BMSBT.Models;

namespace BMSBT.BillServices;

/// <summary>
/// Resolves maintenance bill water charges from dbo.AdditionalCharges (ChargesName = Water Charges).
/// Monthly: include Amount without matching bill month/year. One Time: include Amount only when Month/Year match billing period.
/// </summary>
public static class AdditionalChargeWaterBilling
{
    public const string WaterChargesName = "Water Charges";

    public static decimal SumWaterCharges(IReadOnlyList<AdditionalCharge> rows, string billingMonth, string billingYear)
    {
        if (rows.Count == 0) return 0m;
        decimal sum = 0;
        foreach (var a in rows)
        {
            if (a.ChargesName == null ||
                !string.Equals(a.ChargesName.Trim(), WaterChargesName, StringComparison.OrdinalIgnoreCase))
                continue;

            var freq = a.Frequency?.Trim() ?? string.Empty;
            if (string.Equals(freq, "Monthly", StringComparison.OrdinalIgnoreCase))
            {
                sum += a.Amount;
                continue;
            }

            if (IsOneTimeFrequency(freq) &&
                MonthEquals(billingMonth, a.Month) &&
                YearEquals(billingYear, a.Year))
            {
                sum += a.Amount;
            }
        }

        return sum;
    }

    /// <summary>True if any row for this BTNo is tagged as Water Charges (any frequency).</summary>
    public static bool HasWaterChargeRows(IReadOnlyList<AdditionalCharge> rows) =>
        rows.Any(a =>
            a.ChargesName != null &&
            string.Equals(a.ChargesName.Trim(), WaterChargesName, StringComparison.OrdinalIgnoreCase));

    private static bool IsOneTimeFrequency(string freq) =>
        string.Equals(freq, "One Time", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(freq, "Once", StringComparison.OrdinalIgnoreCase);

    private static bool MonthEquals(string billingMonth, string? rowMonth)
    {
        if (string.IsNullOrWhiteSpace(billingMonth) || string.IsNullOrWhiteSpace(rowMonth)) return false;
        return string.Equals(billingMonth.Trim(), rowMonth.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool YearEquals(string billingYear, string? rowYear)
    {
        if (string.IsNullOrWhiteSpace(billingYear) || string.IsNullOrWhiteSpace(rowYear)) return false;
        return string.Equals(billingYear.Trim(), rowYear.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
