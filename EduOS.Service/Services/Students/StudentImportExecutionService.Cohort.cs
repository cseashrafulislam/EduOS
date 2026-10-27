using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Reconcile up to eight independently bounded files (40,000 source rows)
    /// against committed tenant-owned idempotency records. This is a read-only
    /// status projection, not a cross-file atomic transaction or identity merge.
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

        var result = new StudentImportCohortStatusDto { ExpectedRows = (int)expectedRows };
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await GetJobStatusAsync(file.FileHash, file.MappingHash,
                file.TotalRows, cancellationToken);
            if (!status.Success || status.Data == null)
                return ApiResponse<StudentImportCohortStatusDto>.ErrorResponse(
                    status.Message, status.StatusCode);
            var job = status.Data;
            result.CommittedRows += job.CommittedRows;
            result.Files.Add(new StudentImportCohortFileStatusDto
            {
                FileHash = file.FileHash.ToLowerInvariant(),
                MappingHash = file.MappingHash.ToLowerInvariant(),
                TotalRows = file.TotalRows,
                CommittedRows = job.CommittedRows,
                NextOffset = job.NextOffset,
                MissingWindows = (file.TotalRows + MaxBatchRows - 1) / MaxBatchRows - job.Batches.Count
            });
        }
        return ApiResponse<StudentImportCohortStatusDto>.SuccessResponse(result);
    }
}
