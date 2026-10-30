using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Read-only, bounded cross-file duplicate and guardian identity consistency check.
    /// This never reserves capacity or replaces the per-window transactional validation.
    /// Uploaded content is processed in memory and is not persisted or logged.
    /// </summary>
    public Task<ApiResponse<StudentImportCohortPreflightDto>> PreflightCohortAsync(
        IReadOnlyList<StudentImportCohortUploadDto>? files,
        IReadOnlyDictionary<string, string>? mapping,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Student import access denied.", 403));
        if (files == null || files.Count < 2 || mapping == null)
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Select at least two source files with one shared column mapping.", 400));

        try
        {
            var sourceKeys = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var studentCodes = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var rolls = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var guardians = new Dictionary<string, (string Name, string Phone, int File, int Row)>(
                StringComparer.OrdinalIgnoreCase);
            var result = new StudentImportCohortPreflightDto { FileCount = files.Count };
            var cohortRows = new List<(int FileNumber, ImportRow Row)>();
            long totalBytes = 0;

            for (var index = 0; index < files.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[index];
                if (file == null || file.Content == null || file.Content.Length is < 1 or > 6291456 ||
                    !new[] { ".csv", ".xlsx" }.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Every source must be a non-empty CSV/XLSX file no larger than 6 MB.", 400));
                totalBytes += file.Content.Length;
                if (totalBytes > 8L * 6291456)
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Combined source size exceeds 48 MB.", 413));

                var rows = ReadRows(file.Content, file.Extension, mapping, int.MaxValue);
                if (rows.Count == 0)
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Every source file must contain student rows.", 400));
                if ((long)result.TotalRows + rows.Count > int.MaxValue)
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Import total exceeds numeric representation; validate in separate requests.", 400));
                result.TotalRows += rows.Count;

                var fileNumber = index + 1;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    cohortRows.Add((fileNumber, row));
                    void Duplicate(Dictionary<string, (int File, int Row)> seen, string value, string field)
                    {
                        if (string.IsNullOrWhiteSpace(value)) return;
                        var key = value.Trim();
                        if (seen.TryGetValue(key, out var first))
                            result.Issues.Add(new StudentImportCohortConflictDto
                            {
                                FileNumber = fileNumber, RowNumber = row.RowNumber, Field = field,
                                Message = $"Duplicate {field} (first at file {first.File}, row {first.Row})."
                            });
                        else seen.Add(key, (fileNumber, row.RowNumber));
                    }
                    Duplicate(sourceKeys, row.SourceKey, "SourceKey");
                    Duplicate(studentCodes, row.StudentCode, "StudentCode");
                    if (row.BatchId > 0 && !string.IsNullOrWhiteSpace(row.RollNo))
                        Duplicate(rolls, row.BatchId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                            ":" + row.RollNo.Trim(), "BatchRoll");

                    if (!string.IsNullOrWhiteSpace(row.GuardianSourceKey) &&
                        !string.IsNullOrWhiteSpace(row.GuardianName))
                    {
                        var key = row.GuardianSourceKey.Trim();
                        var name = row.GuardianName.Trim();
                        var phone = row.GuardianPhone.Trim();
                        if (guardians.TryGetValue(key, out var previous))
                        {
                            if (!string.Equals(previous.Name, name, StringComparison.OrdinalIgnoreCase) ||
                                !string.Equals(previous.Phone, phone, StringComparison.Ordinal))
                                result.Issues.Add(new StudentImportCohortConflictDto
                                {
                                    FileNumber = fileNumber, RowNumber = row.RowNumber,
                                    Field = "GuardianSourceKey",
                                    Message = $"Guardian profile differs from file {previous.File}, row {previous.Row}."
                                });
                        }
                        else guardians.Add(key, (name, phone, fileNumber, row.RowNumber));
                    }
                }
            }
            result.TotalIssues = result.Issues.Count;
            return ValidateTenantCohortAsync(result, cohortRows, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Invalid cohort file or column mapping: " + ex.Message, 400));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import cohort preflight failed for tenant {TenantId}", _user.TenantId);
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Unable to check cohort source files.", 500));
        }
    }

    /// <summary>Read-only, tenant-scoped capacity and previously committed guardian-key validation.</summary>
    private async Task<ApiResponse<StudentImportCohortPreflightDto>> ValidateTenantCohortAsync(
        StudentImportCohortPreflightDto result,
        IReadOnlyList<(int FileNumber, ImportRow Row)> cohortRows,
        CancellationToken cancellationToken)
    {
        try
        {
            var tenant = _user.TenantId;
            var batchIds = cohortRows.Select(x => x.Row.BatchId).Where(x => x > 0).Distinct().ToArray();
            var batches = new Dictionary<long, (long CampusId, int Capacity)>();
            var occupancy = new Dictionary<long, int>();
            foreach (var chunk in batchIds.Chunk(200))
            {
                var foundBatches = await _batches.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive && chunk.Contains(x.Id))
                    .Select(x => new { x.Id, x.CampusId, x.Capacity })
                    .ToListAsync(cancellationToken);
                foreach (var batch in foundBatches)
                    batches.Add(batch.Id, (batch.CampusId, batch.Capacity));
                var occupied = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                        x.State == EduOS.Core.Enums.Domain.EnrollmentState.Active &&
                        chunk.Contains(x.AcademicBatchId))
                    .GroupBy(x => x.AcademicBatchId)
                    .Select(x => new { BatchId = x.Key, Count = x.Count() })
                    .ToListAsync(cancellationToken);
                foreach (var count in occupied)
                    occupancy.Add(count.BatchId, count.Count);
            }
            // Reconcile committed source identities without a global Person lookup.
            // Bounded, tenant-indexed SQL lookups; no private identity values in errors.
            var committedSources = new HashSet<Guid>();
            foreach (var chunk in cohortRows.Select(x => x.Row.SourceKey)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(SourceId).Distinct().Chunk(400))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var found = await _keys.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Scope == SourceScope &&
                        !x.IsDeleted && chunk.Contains(x.RequestId))
                    .Select(x => x.RequestId).ToListAsync(cancellationToken);
                committedSources.UnionWith(found);
            }
            var committedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var chunk in cohortRows.Select(x => x.Row.StudentCode)
                .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 50)
                .Distinct(StringComparer.OrdinalIgnoreCase).Chunk(400))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var found = await _students.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && chunk.Contains(x.StudentCode))
                    .Select(x => x.StudentCode).ToListAsync(cancellationToken);
                committedCodes.UnionWith(found);
            }
            foreach (var (fileNumber, row) in cohortRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(row.SourceKey) &&
                    committedSources.Contains(SourceId(row.SourceKey)))
                    result.Issues.Add(new StudentImportCohortConflictDto
                    {
                        FileNumber = fileNumber, RowNumber = row.RowNumber, Field = "SourceKey",
                        Message = "Source key was already committed by this institution."
                    });
                if (!string.IsNullOrWhiteSpace(row.StudentCode) &&
                    committedCodes.Contains(row.StudentCode))
                    result.Issues.Add(new StudentImportCohortConflictDto
                    {
                        FileNumber = fileNumber, RowNumber = row.RowNumber, Field = "StudentCode",
                        Message = "Student code already exists in this institution."
                    });
            }

            var planned = new Dictionary<long, int>();
            foreach (var (fileNumber, row) in cohortRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var validCampus = long.TryParse(row.CampusId, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var campusId) && campusId > 0;
                if (!validCampus || !batches.TryGetValue(row.BatchId, out var batch) ||
                    batch.CampusId != campusId)
                {
                    result.Issues.Add(new StudentImportCohortConflictDto
                    {
                        FileNumber = fileNumber, RowNumber = row.RowNumber, Field = "BatchCampus",
                        Message = "Batch or campus is not available within this institution."
                    });
                    continue;
                }
                var allocated = occupancy.GetValueOrDefault(row.BatchId) + planned.GetValueOrDefault(row.BatchId);
                if (allocated >= batch.Capacity)
                    result.Issues.Add(new StudentImportCohortConflictDto
                    {
                        FileNumber = fileNumber, RowNumber = row.RowNumber, Field = "BatchCapacity",
                        Message = "Combined source files would exceed remaining batch capacity."
                    });
                else planned[row.BatchId] = planned.GetValueOrDefault(row.BatchId) + 1;
            }

            // Only explicit, tenant-owned guardian source keys can be reconciled.
            // Never look up a global Person by name or phone.
            var sourceIds = cohortRows.Where(x => !string.IsNullOrWhiteSpace(x.Row.GuardianSourceKey))
                .Select(x => GuardianSourceId(x.Row.GuardianSourceKey)).Distinct().ToArray();
            var prior = new Dictionary<Guid, (string Hash, long? GuardianId)>();
            foreach (var chunk in sourceIds.Chunk(200))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var records = await _keys.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.Scope == GuardianSourceScope &&
                        !x.IsDeleted && chunk.Contains(x.RequestId))
                    .Select(x => new { x.RequestId, x.RequestHash, x.TargetId })
                    .ToListAsync(cancellationToken);
                foreach (var record in records)
                    prior[record.RequestId] = (record.RequestHash, record.TargetId);
            }
            // A source-key hash is not enough after a guardian profile edit:
            // execution also checks the current tenant-owned guardian name/phone.
            // Keep preflight consistent without disclosing that profile to the caller.
            var activeGuardians = new Dictionary<long, (string Name, string Phone)>();
            foreach (var chunk in prior.Values.Where(x => x.GuardianId.HasValue)
                .Select(x => x.GuardianId!.Value).Distinct().Chunk(200))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var guardians = await _guardians.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive &&
                        chunk.Contains(x.Id))
                    .Select(x => new { x.Id, x.FullName, x.Phone })
                    .ToListAsync(cancellationToken);
                foreach (var guardian in guardians)
                    activeGuardians[guardian.Id] = (guardian.FullName, guardian.Phone ?? "");
            }
            foreach (var (fileNumber, row) in cohortRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(row.GuardianSourceKey) ||
                    !prior.TryGetValue(GuardianSourceId(row.GuardianSourceKey), out var existing))
                    continue;
                if (existing.Hash != GuardianProfileHash(row) ||
                    !existing.GuardianId.HasValue ||
                    !activeGuardians.TryGetValue(existing.GuardianId.Value, out var guardian) ||
                    !string.Equals(guardian.Name.Trim(), row.GuardianName.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(guardian.Phone.Trim(), row.GuardianPhone.Trim(), StringComparison.Ordinal))
                    result.Issues.Add(new StudentImportCohortConflictDto
                    {
                        FileNumber = fileNumber, RowNumber = row.RowNumber, Field = "GuardianSourceKey",
                        Message = "Guardian source key conflicts with an existing tenant-owned guardian."
                    });
            }
            // The same required-subject capacity contract used by transactional
            // 100-row execution must be visible before any multi-file run.
            // This is a read-only warning; execution still revalidates under lock.
            await CheckCohortRequiredOfferingsAsync(result, cohortRows, tenant, cancellationToken);
            result.TotalIssues = result.Issues.Count;
            return ApiResponse<StudentImportCohortPreflightDto>.SuccessResponse(result,
                "Cross-file identity, capacity and guardian references checked; each file still needs transactional validation.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import tenant cohort preflight failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Unable to validate cohort allocations.", 500);
        }
    }
}
