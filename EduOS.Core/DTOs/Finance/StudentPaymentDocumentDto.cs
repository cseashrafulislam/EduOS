namespace EduOS.Core.DTOs.Finance;

// Finance-only projection. Student identity is resolved through the tenant-owned student record.
public sealed class StudentPaymentDocumentDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public StudentPaymentDto Payment { get; set; } = new();
}
