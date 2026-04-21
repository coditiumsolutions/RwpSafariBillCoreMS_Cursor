using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models
{
    /// <summary>Maps to dbo.Adjustments (see db.txt).</summary>
    [Table("Adjustments")]
    public class Adjustment
    {
        [Key]
        public int AdjustmentId { get; set; }

        [MaxLength(50)]
        [Column("BTNo")]
        public string? BtNo { get; set; }

        [MaxLength(50)]
        public string? BillingType { get; set; }

        [MaxLength(50)]
        public string? AdjustmentName { get; set; }

        [MaxLength(50)]
        public string? AdjustmentType { get; set; }

        public int AdjustmentValue { get; set; }

        [MaxLength(20)]
        public string? Frequency { get; set; }

        [MaxLength(20)]
        public string? Month { get; set; }

        [MaxLength(10)]
        public string? Year { get; set; }
    }
}
