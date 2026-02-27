using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models
{
    [Table("AdditionalCharges")]
    public class AdditionalCharge
    {
        [Key]
        [Column("UID")]
        public int Uid { get; set; }

        // Columns from db.txt:
        //  - CustomerNo (nvarchar 50)
        //  - ServiceName (nvarchar 100)
        //  - ServiceType (nvarchar 50)
        //  - Month (nvarchar 20)
        //  - Year (nvarchar 10)

        [MaxLength(50)]
        public string? CustomerNo { get; set; }

        [MaxLength(100)]
        public string? ServiceName { get; set; }

        [MaxLength(50)]
        public string? ServiceType { get; set; }

        [MaxLength(20)]
        public string? Month { get; set; }

        [MaxLength(10)]
        public string? Year { get; set; }
    }
}

