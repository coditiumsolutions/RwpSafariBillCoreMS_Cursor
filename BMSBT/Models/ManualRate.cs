using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models;

public class ManualRate
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int SNo { get; set; }

    [Required(ErrorMessage = "Customer No is required")]
    [StringLength(50)]
    public string CustomerNo { get; set; } = null!;

    [Required(ErrorMessage = "Phase is required")]
    [StringLength(100)]
    public string Phase { get; set; } = null!;

    [StringLength(50)]
    public string? Size { get; set; }

    [Required(ErrorMessage = "Category is required")]
    [StringLength(100)]
    public string Category { get; set; } = null!;

    [Required(ErrorMessage = "Unit Type is required")]
    [StringLength(50)]
    public string UnitType { get; set; } = null!;

    [Range(0, int.MaxValue, ErrorMessage = "Misc cannot be negative")]
    public int Misc { get; set; } = 0;

    [Range(0, int.MaxValue, ErrorMessage = "Tax cannot be negative")]
    public int Tax { get; set; } = 0;

    [Range(0, int.MaxValue, ErrorMessage = "Maint Charges cannot be negative")]
    public int MaintCharges { get; set; } = 0;

    [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
    public int Total { get; private set; }
}
