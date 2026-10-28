namespace EduOS.Core.DTOs.Students;

/// <summary>One committed, bounded tenant-owned student import batch.</summary>
/// <summary>Bounded, ephemeral source content; never persisted as a job attachment.</summary>
public sealed class StudentImportCohortUploadDto
{
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string Extension { get; set; } = string.Empty;
}

public sealed class StudentImportCohortConflictDto
{
    public int FileNumber { get; set; }
    public int RowNumber { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class StudentImportCohortPreflightDto
{
    public int FileCount { get; set; }
    public int TotalRows { get; set; }
    public int TotalIssues { get; set; }
    public bool HasMoreIssues => TotalIssues > Issues.Count;
    public List<StudentImportCohortConflictDto> Issues { get; set; } = new();
}

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

/** One selected source file in a bounded, tenant-owned launch-cohort reconciliation. */
public sealed class StudentImportCohortFileRequestDto
{
    public string FileHash { get; set; } = string.Empty;
    public string MappingHash { get; set; } = string.Empty;
    public int TotalRows { get; set; }
}

public sealed class StudentImportCohortFileStatusDto
{
    public string FileHash { get; set; } = string.Empty;
    public string MappingHash { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int CommittedRows { get; set; }
    public int NextOffset { get; set; }
    public int MissingWindows { get; set; }
    public bool IsComplete => CommittedRows == TotalRows;
}

public sealed class StudentImportCohortStatusDto
{
    public int ExpectedRows { get; set; }
    public int CommittedRows { get; set; }
    public int PendingRows => ExpectedRows - CommittedRows;
    public bool IsComplete => ExpectedRows > 0 && PendingRows == 0;
    public List<StudentImportCohortFileStatusDto> Files { get; set; } = new();
}

public sealed class StudentImportIntegrityIssueDto
{
    public int RowNumber { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public sealed class StudentImportIntegrityReportDto
{
    public Guid ImportReference { get; set; }
    public int ExpectedRows { get; set; }
    public int CheckedRows { get; set; }
    public int HealthyRows { get; set; }
    public bool IsHealthy => CheckedRows == ExpectedRows && HealthyRows == ExpectedRows &&
        Issues.Count == 0;
    public List<StudentImportIntegrityIssueDto> Issues { get; set; } = new();
}
