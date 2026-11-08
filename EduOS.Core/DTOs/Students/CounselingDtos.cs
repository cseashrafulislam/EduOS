namespace EduOS.Core.DTOs.Students;

public sealed class OpenCounselingCaseRequest
{
    public Guid RequestId { get; set; }
    public Guid StudentReference { get; set; }
    public bool Urgent { get; set; }
    public string PrivateSummary { get; set; } = "";
    public bool GuardianContactConsented { get; set; }
}

public sealed class AssignCounselorRequest
{
    public Guid RequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public long CounselorUserId { get; set; }
}

public sealed class CounselingCaseActionRequest
{
    public Guid RequestId { get; set; }
    public string RowVersion { get; set; } = "";
    public string Action { get; set; } = "";
    public string PrivateNote { get; set; } = "";
    public DateTime? AppointmentAtUtc { get; set; }
}

public sealed class CounselingCaseRow
{
    public Guid Reference { get; set; }
    public Guid StudentReference { get; set; }
    public long CampusId { get; set; }
    public string State { get; set; } = "";
    public bool IsUrgent { get; set; }
    public DateTime OpenedAtUtc { get; set; }
    public long? AssignedCounselorUserId { get; set; }
    public bool GuardianContactConsented { get; set; }
    public string RowVersion { get; set; } = "";
}

public sealed class CounselingPrivateDetail
{
    public CounselingCaseRow Case { get; set; } = new();
    public string PrivateSummary { get; set; } = "";
    public IReadOnlyList<CounselingPrivateEvent> Events { get; set; } = [];
}

public sealed class CounselingPrivateEvent
{
    public string Action { get; set; } = "";
    public string Note { get; set; } = "";
    public DateTime? AppointmentAtUtc { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

public sealed class CounselingCasePage
{
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public IReadOnlyList<CounselingCaseRow> Items { get; set; } = [];
}
