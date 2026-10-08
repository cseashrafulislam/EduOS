using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Finance;
// Must be accepted only after an independently verified payout; not an instruction to pay.
public sealed class RecordRefundDisbursementRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(150)] public string PayoutEvidenceReference { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
