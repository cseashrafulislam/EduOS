using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Finance;
/// <summary>Record an independently checked cash payout voucher, never initiate a payment.</summary>
public sealed class RecordRefundDisbursementRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AccountingPeriodId { get; set; }
    public DateOnly PayoutDate { get; set; }
    [Required, MaxLength(100)] public string PayoutEvidenceReference { get; set; } = string.Empty;
    [Required] public string RowVersion { get; set; } = string.Empty;
}
