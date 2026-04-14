using System.Collections.Generic;
using System.Linq;
using BMSBT.Models;
using X.PagedList;

namespace BMSBT.ViewModels
{
    public class MaintenanceCustomerFilterViewModel
    {
        public string? SelectedProject { get; set; }
        public string? SelectedPhase { get; set; }
        public string? SearchBtNo { get; set; }

        public List<string> Projects { get; set; } = new List<string>();
        public List<string> Phases { get; set; } = new List<string>();

        public IPagedList<CustomersMaintenance> Customers { get; set; }
    }
}

