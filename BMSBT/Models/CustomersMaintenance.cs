using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BMSBT.Models
{
    /// <summary>Maps to dbo.CustomersMaintenance (see db.txt).</summary>
    public class CustomersMaintenance
    {
        [Key]
        [Column("uid")]
        public int Uid { get; set; }

        /// <summary>DB column KuickPayNo (not CustomerNo).</summary>
        [Required]
        [StringLength(20)]
        [Column("KuickPayNo")]
        public string CustomerNo { get; set; } = null!;

        [StringLength(20)]
        [Column("BTNo")]
        public string? BTNo { get; set; }

        [StringLength(200)]
        public string? CustomerName { get; set; }

        [StringLength(50)]
        public string? GeneratedMonthYear { get; set; }

        [StringLength(50)]
        public string? LocationSeqNo { get; set; }

        [StringLength(50)]
        [Column("CNICNo")]
        public string? CNICNo { get; set; }

        [StringLength(70)]
        public string? FatherName { get; set; }

        [StringLength(50)]
        public string? MobileNo { get; set; }

        [StringLength(50)]
        public string? City { get; set; }

        [Required]
        [StringLength(50)]
        public string Project { get; set; } = null!;

        /// <summary>DB column Phase.</summary>
        [Required]
        [StringLength(50)]
        [Column("Phase")]
        public string SubProject { get; set; } = null!;

        [Required]
        [StringLength(100)]
        public string Category { get; set; } = null!;

        [StringLength(50)]
        public string? Size { get; set; }

        /// <summary>Optional in the UI. Use <c>string?</c> so nullable reference types do not add implicit [Required] validation; controller persists <c>string.Empty</c> for NOT NULL column.</summary>
        [StringLength(100)]
        public string? Sector { get; set; }

        [Required]
        [StringLength(100)]
        [Column("PloNo")]
        public string PloNo { get; set; } = null!;

        public string? History { get; set; }

        [StringLength(50)]
        public string? BillGenerationStatus { get; set; }

        [StringLength(20)]
        public string? ConnectionStatus { get; set; }

        [StringLength(50)]
        public string? PlotStatus { get; set; }

        [Column("maint")]
        public decimal? Maint { get; set; }

        [Column("misc")]
        public decimal? Misc { get; set; }

        [Column("water")]
        public decimal? Water { get; set; }

        [Column("rent")]
        public decimal? Rent { get; set; }

        [Column("generator")]
        public decimal? Generator { get; set; }

        [Column("other")]
        public decimal? Other { get; set; }

        [Column("foodsafety")]
        public decimal? FoodSafety { get; set; }

        [Column("trollytrip")]
        public decimal? TrollyTrip { get; set; }

        [Column("extrawork")]
        public decimal? ExtraWork { get; set; }

        [StringLength(50)]
        [Column("StreetNumber")]
        public string? StreetNumber { get; set; }

        [StringLength(50)]
        public string? UnitType { get; set; }

        // --- Not persisted (legacy UI / binding); do not use in EF LINQ-to-SQL ---
        [NotMapped] public string? InstalledOn { get; set; }
        [NotMapped] public string? TelephoneNo { get; set; }
        [NotMapped] public string? MeterType { get; set; }
        [NotMapped] public string? NTNNumber { get; set; }
        [NotMapped] public string? TariffName { get; set; }
        [NotMapped] public string? BankNo { get; set; }
        [NotMapped] public string? BTNoMaintenance { get; set; }
        [NotMapped] public string? PlotType { get; set; }
        [NotMapped] public string? BillStatusMaint { get; set; }
        [NotMapped] public string? BillStatus { get; set; }
        [NotMapped] public string? MeterNo { get; set; }
    }
}

