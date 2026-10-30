using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Library;

public sealed class LibraryOverdueNoticeRowDto
{
    public Guid IssueReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string BookTitle { get; set; } = string.Empty;
    public DateOnly DueDate { get; set; }
    public int DaysOverdue { get; set; }
}

public sealed class LibraryOverdueNoticePreviewDto
{
    public long CampusId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int PendingCount { get; set; }
    public IReadOnlyList<LibraryOverdueNoticeRowDto> NextRecipients { get; set; } = [];
}

public sealed class SendLibraryOverdueNoticesRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long CampusId { get; set; }
    public DateOnly AsOfDate { get; set; }
}

public sealed class LibraryOverdueNoticeBatchDto
{
    public Guid ClientRequestId { get; set; }
    public long CampusId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public int SentCount { get; set; }
    public bool AlreadyProcessed { get; set; }
}
