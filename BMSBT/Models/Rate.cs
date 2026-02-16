namespace BMSBT.Models;

/// <summary>
/// Matches table Rates per db.txt: SNo, Phase, Size, Category, UnitType, Misc, Tax, MaintCharges, Total.
/// Phase is matched to CustomersMaintenance.SubProject for MaintCharges lookup in Generate Bill.
/// </summary>
public partial class Rate
{
    public int SNo { get; set; }
    public string? Phase { get; set; }
    public string? Size { get; set; }
    public string? Category { get; set; }
    public string? UnitType { get; set; }
    public int Misc { get; set; }
    public int Tax { get; set; }
    public int MaintCharges { get; set; }
    public int Total { get; set; }
}
