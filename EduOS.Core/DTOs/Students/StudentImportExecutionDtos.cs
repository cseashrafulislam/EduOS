namespace EduOS.Core.DTOs.Students;

/// <summary>One committed, bounded tenant-owned student import batch.</summary>
public sealed class StudentImportExecutionDto
{
    public Guid ImportReference { get; set; }
    public int TotalRows { get; set; }
    public int ImportedRows { get; set; }
    public bool Replayed { get; set; }
}

public sealed class StudentImportReconciliationDto
{
    public Guid ImportReference { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public List<StudentImportReconciliationItemDto> Items { get; set; } = new();
}

public sealed class StudentImportReconciliationItemDto
{
    public int RowNumber { get; set; }
    public string SourceKey { get; set; } = string.Empty;
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

/** Database-reconciled source-file windows; never a browser-only checkpoint. */
public sealed class StudentImportJobStatusDto
{
    public int TotalRows { get; set; }
    public int CommittedRows { get; set; }
    public int NextOffset { get; set; }
    public bool IsComplete => CommittedRows == TotalRows;
    public List<StudentImportJobBatchDto> Batches { get; set; } = new();
}
public sealed class StudentImportJobBatchDto
{
    public int RowOffset { get; set; }
    public int ImportedRows { get; set; }
    public Guid ImportReference { get; set; }
}
