using System.ComponentModel.DataAnnotations;
namespace EduOS.Core.DTOs.Files;
public sealed class DocumentVerificationDto
{
    public Guid Reference { get; set; }
    public Guid FileReference { get; set; }
    public string DocumentTypeCode { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsActive { get; set; }
    public string VerificationStatusCode { get; set; } = "Pending";
    public long? CreatedBy { get; set; }
    public long? VerifiedByUserId { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public string? VerificationNote { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}
public sealed class DocumentVerificationDecisionDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
    public bool Approve { get; set; }
    [MaxLength(500)] public string? Note { get; set; }
}