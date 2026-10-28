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
        if (files == null || files.Count is < 2 or > 8 || mapping == null)
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                "Select two to eight source files with one shared column mapping.", 400));

        try
        {
            var sourceKeys = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var studentCodes = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var rolls = new Dictionary<string, (int File, int Row)>(StringComparer.OrdinalIgnoreCase);
            var guardians = new Dictionary<string, (string Name, string Phone, int File, int Row)>(
                StringComparer.OrdinalIgnoreCase);
            var result = new StudentImportCohortPreflightDto { FileCount = files.Count };
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

                var rows = ReadRows(file.Content, file.Extension, mapping, 5000);
                if (rows.Count == 0)
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Every source file must contain student rows.", 400));
                result.TotalRows += rows.Count;
                if (result.TotalRows > 40000)
                    return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.ErrorResponse(
                        "Cohort preflight is limited to 40,000 rows.", 400));

                var fileNumber = index + 1;
                foreach (var row in rows)
                {
                    cancellationToken.ThrowIfCancellationRequested();
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
            return Task.FromResult(ApiResponse<StudentImportCohortPreflightDto>.SuccessResponse(result,
                "Cross-file source identity check completed; each file still requires tenant-scoped preview and transactional validation."));
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
}
