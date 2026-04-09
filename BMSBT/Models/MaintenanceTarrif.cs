using System;
using System.Collections.Generic;

namespace BMSBT.Models;

public partial class MaintenanceTarrif
{
    public int Uid { get; set; }

    public string Project { get; set; } = null!;

    public string Category { get; set; } = null!;

    public string Size { get; set; } = null!;

    public double Charges { get; set; }

    public string? Tax { get; set; }
}
