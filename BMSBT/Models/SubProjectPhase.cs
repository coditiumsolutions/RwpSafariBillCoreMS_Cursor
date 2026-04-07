using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models;

/// <summary>
/// Read-only mapping to dbo.SubProject: distinct Phase_Number values per Project for dropdowns.
/// </summary>
public class SubProjectPhase
{
    public string? Project { get; set; }

    [Column("Phase_Number")]
    public string? PhaseNumber { get; set; }
}
