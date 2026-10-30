using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Reconcile tenant-owned source files with SQL-safe chunked window queries.
    /// File and student counts are source-driven; audit reads contain no learner PII.
    /// </summary>
    public async Task<ApiResponse<StudentImportCohortStatusDto>> GetCohortStatusAsync(
        IReadOnlyList<StudentImportCohortFileRequestDto>? files,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse("Student import access denied.", 403);
        if (files == null || files.Count < 1)
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Select at least one source file.", 400);

        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long expectedRows = 0;
        foreach (var file in files)
        {
            if (file == null || !IsSha256Hex(file.FileHash) || !IsSha256Hex(file.MappingHash) ||
                file.TotalRows < 1 ||
                !unique.Add(file.FileHash.ToLowerInvariant() + ":" + file.MappingHash.ToLowerInvariant()))
                return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                    "Invalid or duplicate source fingerprint, mapping, or row count.", 400);
            expectedRows += file.TotalRows;
        }
        if (expectedRows > int.MaxValue)
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Cohort total exceeds the supported numeric representation. Reconcile separate manifests.", 400);

        // Request identifiers are deterministic for each original source fingerprint,
        // mapping and 100-row offset. Never trust a caller-provided target/log ID.
        var plans = files.Select(file => (
            File: file,
            Windows: Enumerable.Range(0, (int)(((long)file.TotalRows + MaxBatchRows - 1) / MaxBatchRows))
                .Select(i => (Offset: i * MaxBatchRows,
                    RequestId: JobRequestId(file.FileHash.ToLowerInvariant(),
                        file.MappingHash.ToLowerInvariant(), i * MaxBatchRows)))
                .ToArray())).ToArray();
        var requestIds = plans.SelectMany(x => x.Windows.Select(w => w.RequestId)).ToArray();
        var tenant = _user.TenantId;
        cancellationToken.ThrowIfCancellationRequested();

        // Query chunks never become a business cap: 400 GUIDs is an SQL parameter budget.
        var keys = new List<(Guid RequestId, long TargetId)>();
        foreach (var chunk in requestIds.Chunk(400))
        {
            var found = await _keys.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Scope == BatchScope && !x.IsDeleted &&
                    x.CompletedAt.HasValue && x.TargetId.HasValue && chunk.Contains(x.RequestId))
                .Select(x => new { x.RequestId, x.TargetId }).ToListAsync(cancellationToken);
            keys.AddRange(found.Select(x => (x.RequestId, x.TargetId!.Value)));
        }
        // Fail closed rather than throwing ToDictionary or counting a shared
        // import log as two separately committed source windows.
        if (keys.GroupBy(x => x.RequestId).Any(g => g.Count() != 1) ||
            keys.GroupBy(x => x.TargetId).Any(g => g.Count() != 1))
            return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                "Import batch receipts are ambiguous. Review committed history before reconciliation.", 409);
        var logIds = keys.Select(x => x.TargetId).Distinct().ToArray();
        var logs = new Dictionary<long, (int Total, int Success, int Failed, Guid Reference)>();
        var itemCounts = new Dictionary<long, (int Rows, int Success, int WithSource)>();
        foreach (var chunk in logIds.Chunk(400))
        {
            var foundLogs = await _logs.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                    x.CompletedAt.HasValue && chunk.Contains(x.Id))
                .Select(x => new { x.Id, x.PublicId, x.TotalRows, x.SuccessRows, x.FailedRows })
                .ToListAsync(cancellationToken);
            foreach (var log in foundLogs)
                logs.Add(log.Id, (log.TotalRows, log.SuccessRows, log.FailedRows, log.PublicId));
            // Audit counters alone are not enough; verify actual source log items.
            var foundItems = await _items.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && chunk.Contains(x.ImportLogId))
                .GroupBy(x => x.ImportLogId)
                .Select(g => new
                {
                    LogId = g.Key, Rows = g.Count(), Success = g.Count(x => x.IsSuccess),
                    WithSource = g.Count(x => x.BusinessKey != null && x.BusinessKey.Trim() != "")
                }).ToListAsync(cancellationToken);
            foreach (var item in foundItems)
                itemCounts.Add(item.LogId, (item.Rows, item.Success, item.WithSource));
        }
        var keyTargets = keys.ToDictionary(x => x.RequestId, x => x.TargetId);

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
