namespace BMSBT.ViewModels
{
    public class CollectionsSummaryRowViewModel
    {
        public string Status { get; set; } = "";
        public int TotalBillsGenerated { get; set; }
        public decimal TotalAmountGenerated { get; set; }
        public decimal TotalAmountCollected { get; set; }
    }
}
