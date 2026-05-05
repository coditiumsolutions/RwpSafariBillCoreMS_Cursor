namespace BMSBT.ViewModels
{
    public class CustomersSummaryRowViewModel
    {
        public string Project { get; set; } = "";
        public string Phase { get; set; } = "";
        public int Customers { get; set; }
        public int BillsGenerated { get; set; }
        public decimal BillAmountGenerated { get; set; }
    }
}
