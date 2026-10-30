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
    public int PageOffset { get; set; }
    public int NextPageOffset { get; set; }
    public bool HasMorePages => NextPageOffset < TotalRows;
    public int CommittedRows { get; set; }
    public int NextOffset { get; set; }
    public int ReviewWindows { get; set; }
    public bool RequiresReview => ReviewWindows > 0;
    public bool IsComplete => CommittedRows == TotalRows;
    public bool IsLogItemReconciled => IsComplete && !RequiresReview;
    public List<StudentImportJobBatchDto> Batches { get; set; } = new();
}
public sealed class StudentImportJobBatchDto
{
    public int RowOffset { get; set; }
    public int ImportedRows { get; set; }
    public Guid ImportReference { get; set; }
    public bool LogItemsVerified { get; set; }
    public string LogItemAuditCode { get; set; } = "NotChecked";
}

/** One selected source file in a bounded, tenant-owned launch-cohort reconciliation. */
public sealed class StudentImportCohortFileRequestDto
{
    public string FileHash { get; set; } = string.Empty;
    public string MappingHash { get; set; } = string.Empty;
    public int TotalRows { get; set; }
}

public sealed class StudentImportCohortWindowStatusDto
{
    public int RowOffset { get; set; }
    public int ImportedRows { get; set; }
    public Guid ImportReference { get; set; }
    // Only physical import-log items, not learner/guardian/academic integrity.
    public bool LogItemsVerified { get; set; }
    public string LogItemAuditCode { get; set; } = "NotChecked";
}

public sealed class StudentImportCohortFileStatusDto
{
    public string FileHash { get; set; } = string.Empty;
    public string MappingHash { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public int CommittedRows { get; set; }
    public int NextOffset { get; set; }
    public int MissingWindows { get; set; }
    public int LogItemsVerifiedRows { get; set; }
    public int LogItemReviewWindows { get; set; }
    public bool IsLogItemReconciled => IsComplete && LogItemReviewWindows == 0 &&
        LogItemsVerifiedRows == CommittedRows;
    public List<StudentImportCohortWindowStatusDto> CompletedWindows { get; set; } = new();
    public bool IsComplete => CommittedRows == TotalRows;
}

public sealed class StudentImportCohortStatusDto
{
    public int ExpectedRows { get; set; }
    public int CommittedRows { get; set; }
    public int LogItemsVerifiedRows { get; set; }
    public int LogItemReviewWindows { get; set; }
    public bool IsLogItemReconciled => IsComplete && LogItemReviewWindows == 0 &&
        LogItemsVerifiedRows == CommittedRows;
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
    public int SourceReceiptsChecked { get; set; }
    public int SourceReceiptsHealthy { get; set; }
    public int GuardianLinksChecked { get; set; }
    public int GuardianLinksHealthy { get; set; }
    public int CurrentEnrollmentsChecked { get; set; }
    public int AcademicRowsSkipped { get; set; }
    public int RequiredSubjectsExpected { get; set; }
    public int RequiredSubjectsHealthy { get; set; }
    public bool IsHealthy => CheckedRows == ExpectedRows && HealthyRows == ExpectedRows &&
        SourceReceiptsChecked == ExpectedRows && SourceReceiptsHealthy == SourceReceiptsChecked &&
        GuardianLinksChecked == GuardianLinksHealthy &&
        RequiredSubjectsExpected == RequiredSubjectsHealthy && Issues.Count == 0;
    public List<StudentImportIntegrityIssueDto> Issues { get; set; } = new();
}

/// <summary>
/// A bounded, PII-free reconciliation of the CURRENT enrollment of students
/// from one completed import window. A preview is not authorization to mutate.
/// </summary>
public sealed class StudentImportAcademicRepairPlanDto
{
    public Guid ImportReference { get; set; }
    public string PlanHash { get; set; } = string.Empty;
    public int ImportedRowsChecked { get; set; }
    public int MissingRegistrations { get; set; }
    public List<StudentImportIntegrityIssueDto> Blockers { get; set; } = new();
    public bool CanApply => MissingRegistrations > 0 && Blockers.Count == 0;
}

public sealed class StudentImportAcademicRepairResultDto
{
    public Guid ImportReference { get; set; }
    public int CreatedRegistrations { get; set; }
    public bool Replayed { get; set; }
}
