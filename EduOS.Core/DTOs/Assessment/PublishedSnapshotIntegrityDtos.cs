using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Assessment;

/// <summary>Bounded, authorized inspection of a finalized historical result version.</summary>
public sealed class PublishedSnapshotIntegrityQueryDto
{
    [Range(1, 100000)] public int Page { get; set; } = 1;
    [Range(1, 50)] public int PageSize { get; set; } = 25;
}

public sealed class PublishedSnapshotIssueDto
{
    public long SummaryId { get; set; }
    public long StudentEnrollmentId { get; set; }
    public string IssueCode { get; set; } = string.Empty;
}

public sealed class PublishedSnapshotIntegrityPageDto
{
    public long PublicationId { get; set; }
    public int VersionNo { get; set; }
    public int SnapshotCount { get; set; }
    public int InvalidCount { get; set; }
    public bool IsReconciled { get; set; }
    public bool IsEmptySnapshot { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public IReadOnlyList<PublishedSnapshotIssueDto> Items { get; set; } =
        Array.Empty<PublishedSnapshotIssueDto>();
}
