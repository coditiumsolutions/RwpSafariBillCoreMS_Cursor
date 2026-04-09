using BMSBT.Models;
using Microsoft.EntityFrameworkCore;

namespace BMSBT.BillServices;

/// <summary>
/// Resolves maintenance bill amounts from <c>MaintenanceTarrif</c> by Project, Category, and Size.
/// </summary>
public static class MaintenanceTariffLookup
{
    /// <summary>
    /// Finds a tariff row by Project + Category + Size.
    /// First tries an exact three-way match (case-insensitive, trimmed).
    /// If no size-specific row is found, falls back to the first row matching
    /// Project + Category only — so bills are never blocked when the tariff
    /// table has a single "catch-all" rate per category.
    /// </summary>
    public static MaintenanceTarrif? FindTariff(BmsbtContext db, string? project, string? category, string? size)
    {
        var p = project?.Trim() ?? "";
        var c = category?.Trim() ?? "";
        var s = size?.Trim() ?? "";

        if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(c))
            return null;

        // Load all rows for this Project + Category once to avoid multiple DB round-trips
        var candidates = db.MaintenanceTarrifs
            .AsEnumerable()
            .Where(t =>
                string.Equals(t.Project?.Trim(), p, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(t.Category?.Trim(), c, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
            return null;

        // 1. Exact size match (preferred)
        if (!string.IsNullOrEmpty(s))
        {
            var exact = candidates.FirstOrDefault(t =>
                string.Equals(t.Size?.Trim(), s, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
                return exact;
        }

        // 2. Fallback: first tariff row for this Project + Category
        return candidates[0];
    }

    public static decimal ParseTaxAmount(string? tax)
    {
        if (string.IsNullOrWhiteSpace(tax))
            return 0m;
        var s = tax.Trim();
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d))
            return d;
        if (decimal.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.CurrentCulture, out d))
            return d;
        return 0m;
    }
}
