namespace BMSBT.DTO
{
    public class BillViewModel
    {
        public string? BillingMonth { get; set; }
        public string? BillingYear { get; set; }
        public string? MeteringType { get; set; }
        public DateTime? PaidOn { get; set; }
        /// <summary>Target payment status when updating (Paid, Unpaid, Paid with surcharge).</summary>
        public string? PaymentType { get; set; }
        public string? BankBranch { get; set; }
        public string? Btno { get; set; }
        public string? ReferenceNumber { get; set; }
        public string? Name { get; set; }
        public string? CustomerName { get; set; }

        public string? CurrentPaymentStatus { get; set; }
        public int? BillUid { get; set; }
        public int? BillAmountInDueDate { get; set; }
        public int? BillSurcharge { get; set; }
        public int? BillAmountAfterDueDate { get; set; }
    }
}
