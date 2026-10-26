using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Administrative fines are not invoices or GL revenue until an audited posting workflow exists.</summary>
public sealed class StudentFineRegisterQueryDto
{
    public long? CampusId { get; set; }
    public DateOnly? From { get; set; }
    public DateOnly? To { get; set; }
    public bool? Waived { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 25;
}

public sealed class StudentFineRegisterRowDto
{
    public long Id { get; set; }
    public Guid EnrollmentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public long CampusId { get; set; }
    public string RuleName { get; set; } = string.Empty;
    public DateOnly FineDate { get; set; }
    public decimal Amount { get; set; }
    public string? Reason { get; set; }
    public bool IsWaived { get; set; }
    public long? WaivedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class StudentFineRegisterDto
{
    public PagedResult<StudentFineRegisterRowDto> Fines { get; set; } = new();
    public decimal RecordedTotal { get; set; }
    public decimal WaivedTotal { get; set; }
    public decimal OutstandingAdministrativeTotal { get; set; }
    public bool IsPostedToLedger { get; set; } = false;
}
