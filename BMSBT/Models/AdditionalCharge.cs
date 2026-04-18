using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models
{
    /// <summary>Maps to dbo.AdditionalCharges (see db.txt).</summary>
    [Table("AdditionalCharges")]
    public class AdditionalCharge
    {
        [Key]
        [Column("UID")]
        public int Uid { get; set; }

        [MaxLength(50)]
        [Column("BTNo")]
        public string? BtNo { get; set; }

        [MaxLength(60)]
        public string? Department { get; set; }

        [MaxLength(60)]
        public string? ServiceType { get; set; }

        [MaxLength(60)]
        public string? ChargesName { get; set; }

        public int Amount { get; set; }

        [MaxLength(50)]
        public string? Frequency { get; set; }

        [MaxLength(20)]
        public string? Month { get; set; }

        [MaxLength(10)]
        public string? Year { get; set; }
    }
}
