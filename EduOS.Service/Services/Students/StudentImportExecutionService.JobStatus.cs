using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Recover committed source windows from tenant-scoped idempotency records.
    /// No uploaded file or learner PII is stored; at most 50 windows are queried.
    /// </summary>
    public async Task<ApiResponse<StudentImportJobStatusDto>> GetJobStatusAsync(
        string fileHash, string mappingHash, int totalRows, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportJobStatusDto>.ErrorResponse("Student import access denied.", 403);
        if (totalRows is < 1 or > 5000 || !IsSha256Hex(fileHash) || !IsSha256Hex(mappingHash))
            return ApiResponse<StudentImportJobStatusDto>.ErrorResponse("Invalid source fingerprint or row count.", 400);

        fileHash = fileHash.ToLowerInvariant();
        mappingHash = mappingHash.ToLowerInvariant();
        var offsets = Enumerable.Range(0, (totalRows + MaxBatchRows - 1) / MaxBatchRows)
            .Select(i => i * MaxBatchRows).ToArray();
        var requests = offsets.Select(offset => new
        {
            Offset = offset, RequestId = JobRequestId(fileHash, mappingHash, offset)
        }).ToArray();
        var ids = requests.Select(x => x.RequestId).ToArray();
        var tenant = _user.TenantId;
        var keys = await _keys.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Scope == BatchScope && !x.IsDeleted &&
                x.CompletedAt.HasValue && x.TargetId.HasValue && ids.Contains(x.RequestId))
            .Select(x => new { x.RequestId, x.TargetId }).ToListAsync(cancellationToken);
        var logIds = keys.Select(x => x.TargetId!.Value).Distinct().ToArray();
        var logs = await _logs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                x.CompletedAt.HasValue && logIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PublicId, x.TotalRows, x.SuccessRows, x.FailedRows })
            .ToListAsync(cancellationToken);
        var byRequest = keys.ToDictionary(x => x.RequestId, x => x.TargetId!.Value);
        var byLog = logs.ToDictionary(x => x.Id);
        var completed = new List<StudentImportJobBatchDto>();
        foreach (var request in requests)
        {
            if (!byRequest.TryGetValue(request.RequestId, out var logId) ||
                !byLog.TryGetValue(logId, out var log)) continue;
            var expected = Math.Min(MaxBatchRows, totalRows - request.Offset);
            if (log.TotalRows != expected || log.SuccessRows != expected || log.FailedRows != 0)
                continue;
            completed.Add(new StudentImportJobBatchDto
            {
                RowOffset = request.Offset, ImportedRows = expected, ImportReference = log.PublicId
            });
        }
        var completeOffsets = completed.Select(x => x.RowOffset).ToHashSet();
        return ApiResponse<StudentImportJobStatusDto>.SuccessResponse(new StudentImportJobStatusDto
        {
            TotalRows = totalRows,
            CommittedRows = completed.Sum(x => x.ImportedRows),
            NextOffset = offsets.FirstOrDefault(x => !completeOffsets.Contains(x), totalRows),
            Batches = completed
        });
    }

    private static bool IsSha256Hex(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    // Matches wwwroot/js/students/import-job.js UUID bytes.
    private static Guid JobRequestId(string fileHash, string mappingHash, int offset)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(
            fileHash + ":" + mappingHash + ":" + offset.ToString(CultureInfo.InvariantCulture)));
        digest[6] = (byte)((digest[6] & 0x0f) | 0x50);
        digest[8] = (byte)((digest[8] & 0x3f) | 0x80);
        var hex = Convert.ToHexString(digest.AsSpan(0, 16));
        return Guid.ParseExact(
            $"{hex[..8]}-{hex[8..12]}-{hex[12..16]}-{hex[16..20]}-{hex[20..]}", "D");
    }
}
