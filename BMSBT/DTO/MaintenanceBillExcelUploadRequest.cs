using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace BMSBT.DTO;

public class MaintenanceBillExcelUploadRequest
{
    [Required]
    public IFormFile? ExcelFile { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Start row number must be greater than zero.")]
    public int StartRowNumber { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "End row number must be greater than zero.")]
    public int EndRowNumber { get; set; }
}
