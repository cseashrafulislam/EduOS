using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Files;
// Only a trusted malware scanning integration may submit this command.
public sealed class RecordFileSecurityScanRequestDto
{
    public bool IsVerifiedSafe { get; set; }
    [Required, MaxLength(30)] public string ScanStateCode { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
