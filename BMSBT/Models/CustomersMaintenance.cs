using System.ComponentModel.DataAnnotations;

namespace BMSBT.Models
{
    public class CustomersMaintenance
    {
        [Key]
        public int Uid { get; set; }

        [Required]
        [StringLength(20)]
        public string CustomerNo { get; set; }

        [StringLength(20)]
        public string? BTNo { get; set; }

        [StringLength(70)]
        public string? CustomerName { get; set; }

        [StringLength(50)]
        public string? GeneratedMonthYear { get; set; }

        [StringLength(50)]
        public string? LocationSeqNo { get; set; }

        [StringLength(50)]
        public string? CNICNo { get; set; }

        [StringLength(70)]
        public string? FatherName { get; set; }

        [StringLength(20)]
        public string? InstalledOn { get; set; }

        [StringLength(50)]
        public string? MobileNo { get; set; }

        [StringLength(50)]
        public string? TelephoneNo { get; set; }

        [StringLength(50)]
        public string? MeterType { get; set; }

        [StringLength(50)]
        public string? NTNNumber { get; set; }

        [StringLength(50)]
        public string? City { get; set; }

        [Required]
        [StringLength(50)]
        public string Project { get; set; }

        [Required]
        [StringLength(50)]
        public string SubProject { get; set; }

        [Required]
        [StringLength(50)]
        public string TariffName { get; set; }

        [StringLength(50)]
        public string? BankNo { get; set; }

        [StringLength(30)]
        public string? BTNoMaintenance { get; set; }

        [Required]
        [StringLength(100)]
        public string Category { get; set; }

        [Required]
        [StringLength(100)]
        public string Block { get; set; }

        [StringLength(50)]
        public string? PlotType { get; set; }

        [StringLength(50)]
        public string? Size { get; set; }

        [Required]
        [StringLength(100)]
        public string Sector { get; set; }

        [Required]
        [StringLength(100)]
        public string PloNo { get; set; }

        public string? BillStatusMaint { get; set; }

        public string? BillStatus { get; set; }

        public string? History { get; set; }

        [StringLength(50)]
        public string? BillGenerationStatus { get; set; }

        [StringLength(50)]
        public string? MeterNo { get; set; }

        /// <summary>Connection status; "Disconnected" skips bill generation (LotusScript logic).</summary>
        [StringLength(50)]
        public string? ConnectionStatus { get; set; }
    }


}

