using EduOS.Core.Common;

namespace EduOS.Core.DTOs.Finance;

/// <summary>Register covers both unposted assessments and separately posted fine invoices.</summary>
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
    public Guid? BilledInvoiceReference { get; set; }
    public Guid? ReversalReference { get; set; }
    public JournalState? ReversalState { get; set; }
    public string? ReversalRowVersion { get; set; }
    public Guid? SourceInvoiceReference { get; set; }
    public int? ChargedOverdueDays { get; set; }
    public long? WaivedByUserId { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class StudentFineRegisterDto
{
    public PagedResult<StudentFineRegisterRowDto> Fines { get; set; } = new();
    public decimal RecordedTotal { get; set; }
    public decimal WaivedTotal { get; set; }
    public decimal OutstandingAdministrativeTotal { get; set; }
    public decimal PostedFineTotal { get; set; }
    public bool IsPostedToLedger { get; set; } = false;
}
