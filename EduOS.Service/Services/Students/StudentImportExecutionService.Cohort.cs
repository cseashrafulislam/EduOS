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
        var logIds = keys.Select(x => x.TargetId!.Value).Distinct().ToArray();
        var logs = logIds.Length == 0
            ? new Dictionary<long, (int Total, int Success, int Failed, Guid Reference)>()
            : (await _logs.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                    x.CompletedAt.HasValue && logIds.Contains(x.Id))
                .Select(x => new { x.Id, x.PublicId, x.TotalRows, x.SuccessRows, x.FailedRows })
                .ToListAsync(cancellationToken))
                .ToDictionary(x => x.Id, x => (Total: x.TotalRows, Success: x.SuccessRows, Failed: x.FailedRows, Reference: x.PublicId));
        var keyTargets = keys.ToDictionary(x => x.RequestId, x => x.TargetId!.Value);

        var result = new StudentImportCohortStatusDto { ExpectedRows = (int)expectedRows };
        foreach (var plan in plans)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var committed = 0;
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
                    completed.Add(new StudentImportCohortWindowStatusDto
                    {
                        RowOffset = window.Offset, ImportedRows = expected,
                        ImportReference = log.Reference
                    });
                }
                else if (nextOffset == plan.File.TotalRows)
                    nextOffset = window.Offset;
            }
            result.CommittedRows += committed;
            result.Files.Add(new StudentImportCohortFileStatusDto
            {
                FileHash = plan.File.FileHash.ToLowerInvariant(),
                MappingHash = plan.File.MappingHash.ToLowerInvariant(),
                TotalRows = plan.File.TotalRows,
                CommittedRows = committed,
                NextOffset = nextOffset,
                MissingWindows = plan.Windows.Length - completedWindows,
                CompletedWindows = completed
            });
        }
        return ApiResponse<StudentImportCohortStatusDto>.SuccessResponse(result);
    }
}
