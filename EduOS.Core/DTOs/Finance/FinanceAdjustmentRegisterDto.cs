namespace EduOS.Core.DTOs.Finance;

public sealed class FinanceEnrollmentOptionDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string RollNo { get; set; } = string.Empty;
}
/// <summary>Tenant-scoped enrollment adjustment register; fines here are not posted invoices.</summary>
public sealed class FinanceAdjustmentRegisterDto
{
    public IReadOnlyList<StudentDiscountDto> Discounts { get; set; } = Array.Empty<StudentDiscountDto>();
    public IReadOnlyList<StudentFineDto> Fines { get; set; } = Array.Empty<StudentFineDto>();
}
