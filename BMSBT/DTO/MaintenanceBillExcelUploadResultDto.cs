namespace BMSBT.DTO;

public class MaintenanceBillExcelUploadResultDto
{
    public int TotalProcessedRows { get; set; }
    public int SuccessfullyUpdatedRecords { get; set; }
    public int FailedOrNotFoundRecords { get; set; }
    public List<MaintenanceBillExcelFailureDto> MissingRecords { get; set; } = new();
}

public class MaintenanceBillExcelFailureDto
{
    public int RowNumber { get; set; }
    public string Btno { get; set; } = string.Empty;
    public string BillingMonth { get; set; } = string.Empty;
    public string BillingYear { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
