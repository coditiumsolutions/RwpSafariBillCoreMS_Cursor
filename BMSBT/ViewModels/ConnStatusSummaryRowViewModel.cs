namespace BMSBT.ViewModels
{
    public class ConnStatusSummaryRowViewModel
    {
        public string Project { get; set; } = "";
        public int Connected { get; set; }
        public int Disconnected { get; set; }
        public int Closed { get; set; }
        public int Total { get; set; }
    }
}
