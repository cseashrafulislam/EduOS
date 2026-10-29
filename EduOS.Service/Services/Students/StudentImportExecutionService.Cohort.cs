using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Reconcile at most 400 committed 100-row windows across eight source files.
    /// One tenant-scoped key projection and one log projection replace the former
    /// per-file status queries. This never reads learner PII or writes import state.
    /// </summary>
    public async Task<ApiResponse<StudentImportCohortStatusDto>> GetCohortStatusAsync(
        IReadOnlyList<StudentImportCohortFileRequestDto>? files,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse("Student import access denied.", 403);
        if (files == null || files.Count is < 1 or > 8)
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Select between one and eight source files.", 400);

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expectedRows = 0;
        foreach (var file in files)
        {
            if (file == null || !IsSha256Hex(file.FileHash) || !IsSha256Hex(file.MappingHash) ||
                file.TotalRows is < 1 or > 5000 ||
                !unique.Add(file.FileHash.ToLowerInvariant() + ":" + file.MappingHash.ToLowerInvariant()))
                return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                    "Invalid or duplicate source fingerprint, mapping, or row count.", 400);
            expectedRows += file.TotalRows;
        }
        if (expectedRows > 40000)
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Cohort reconciliation is limited to 40,000 source rows.", 400);

        // Request identifiers are deterministic for each original source fingerprint,
        // mapping and 100-row offset. Never trust a caller-provided target/log ID.
        var plans = files.Select(file => (
            File: file,
            Windows: Enumerable.Range(0, (file.TotalRows + MaxBatchRows - 1) / MaxBatchRows)
                .Select(i => (Offset: i * MaxBatchRows,
                    RequestId: JobRequestId(file.FileHash.ToLowerInvariant(),
                        file.MappingHash.ToLowerInvariant(), i * MaxBatchRows)))
                .ToArray())).ToArray();
        var requestIds = plans.SelectMany(x => x.Windows.Select(w => w.RequestId)).ToArray();
        var tenant = _user.TenantId;
        cancellationToken.ThrowIfCancellationRequested();

        // At most 400 GUIDs; bounded well below SQL Server's parameter limit.
        // The tenant/scope predicate is enforced in SQL, not after materialization.
        var keys = await _keys.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Scope == BatchScope && !x.IsDeleted &&
                x.CompletedAt.HasValue && x.TargetId.HasValue && requestIds.Contains(x.RequestId))
            .Select(x => new { x.RequestId, x.TargetId })
            .ToListAsync(cancellationToken);
        // Fail closed rather than throwing ToDictionary or counting a shared
        // import log as two separately committed source windows.
        if (keys.GroupBy(x => x.RequestId).Any(g => g.Count() != 1) ||
            keys.GroupBy(x => x.TargetId!.Value).Any(g => g.Count() != 1))
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Import batch receipts are ambiguous. Review committed history before reconciliation.", 409);
        var logIds = keys.Select(x => x.TargetId!.Value).Distinct().ToArray();
        var logs = logIds.Length == 0
            ? new Dictionary<long, (int Total, int Success, int Failed, Guid Reference)>()
            : (await _logs.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                    x.CompletedAt.HasValue && logIds.Contains(x.Id))
                .Select(x => new { x.Id, x.PublicId, x.TotalRows, x.SuccessRows, x.FailedRows })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => (Total: x.TotalRows, Success: x.SuccessRows, Failed: x.FailedRows, Reference: x.PublicId));
        // One bounded tenant-scoped SQL aggregate, not 400 per-window queries.
        // Log counters alone cannot prove that source audit rows still exist.
        var itemCounts = logIds.Length == 0
            ? new Dictionary<long, (int Rows, int Success, int WithSource)>()
            : (await _items.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && logIds.Contains(x.ImportLogId))
                .GroupBy(x => x.ImportLogId)
                .Select(g => new
                {
                    ImportLogId = g.Key,
                    Rows = g.Count(),
                    Success = g.Count(x => x.IsSuccess),
                    WithSource = g.Count(x => x.BusinessKey != null && x.BusinessKey.Trim() != "")
                })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.ImportLogId, x => (x.Rows, x.Success, x.WithSource));
        var keyTargets = keys.ToDictionary(x => x.RequestId, x => x.TargetId!.Value);

        var result = new StudentImportCohortStatusDto { ExpectedRows = (int)expectedRows };
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var committed = 0;
            var logItemsVerified = 0;
            var logItemReviewWindows = 0;
            var completedWindows = 0;
            var nextOffset = plan.File.TotalRows;
            var completed = new List<StudentImportCohortWindowStatusDto>(plan.Windows.Length);
            foreach (var window in plan.Windows)
            {
                var expected = Math.Min(MaxBatchRows, plan.File.TotalRows - window.Offset);
                if (keyTargets.TryGetValue(window.RequestId, out var logId) &&
                    logs.TryGetValue(logId, out var log) &&
                    log.Total == expected && log.Success == expected && log.Failed == 0)
                {
                    committed += expected;
                    completedWindows++;
                    var auditCode = !itemCounts.TryGetValue(logId, out var physical)
                        ? "MissingLogItems"
                        : physical.Rows != expected ? "RowCountMismatch"
                        : physical.Success != expected ? "UnsuccessfulRows"
                        : physical.WithSource != expected ? "MissingSourceKeys"
                        : "Verified";
                    var verified = auditCode == "Verified";
                    if (verified) logItemsVerified += expected;
                    else logItemReviewWindows++;
                    completed.Add(new StudentImportCohortWindowStatusDto
                    {
                        RowOffset = window.Offset, ImportedRows = expected,
                        ImportReference = log.Reference,
                        LogItemsVerified = verified, LogItemAuditCode = auditCode
                    });
                }
                else if (nextOffset == plan.File.TotalRows)
                    nextOffset = window.Offset;
            }
            result.CommittedRows += committed;
            result.LogItemsVerifiedRows += logItemsVerified;
            result.LogItemReviewWindows += logItemReviewWindows;
            result.Files.Add(new StudentImportCohortFileStatusDto
            {
                FileHash = plan.File.FileHash.ToLowerInvariant(),
                MappingHash = plan.File.MappingHash.ToLowerInvariant(),
                TotalRows = plan.File.TotalRows,
                CommittedRows = committed,
                LogItemsVerifiedRows = logItemsVerified,
                LogItemReviewWindows = logItemReviewWindows,
                NextOffset = nextOffset,
                MissingWindows = plan.Windows.Length - completedWindows,
                CompletedWindows = completed
            });
        }
        return ApiResponse<StudentImportCohortStatusDto>.SuccessResponse(result);
    }
}
