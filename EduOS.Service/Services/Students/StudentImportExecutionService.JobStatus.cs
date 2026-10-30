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
    /// No uploaded file or learner PII is stored; windows are queried in bounded SQL chunks.
    /// </summary>
    public async Task<ApiResponse<StudentImportJobStatusDto>> GetJobStatusAsync(
        string fileHash, string mappingHash, int totalRows, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportJobStatusDto>.ErrorResponse("Student import access denied.", 403);
        if (totalRows < 1 || !IsSha256Hex(fileHash) || !IsSha256Hex(mappingHash))
            return ApiResponse<StudentImportJobStatusDto>.ErrorResponse("Invalid source fingerprint or row count.", 400);

        fileHash = fileHash.ToLowerInvariant();
        mappingHash = mappingHash.ToLowerInvariant();
        var offsets = Enumerable.Range(0, (int)(((long)totalRows + MaxBatchRows - 1) / MaxBatchRows))
            .Select(i => i * MaxBatchRows).ToArray();
        var requests = offsets.Select(offset => new
        {
            Offset = offset, RequestId = JobRequestId(fileHash, mappingHash, offset)
        }).ToArray();
        var ids = requests.Select(x => x.RequestId).ToArray();
        var tenant = _user.TenantId;
        var keys = new List<(Guid RequestId, long TargetId)>();
        foreach (var chunk in ids.Chunk(400))
        {
            var found = await _keys.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.Scope == BatchScope && !x.IsDeleted &&
                    x.CompletedAt.HasValue && x.TargetId.HasValue && chunk.Contains(x.RequestId))
                .Select(x => new { x.RequestId, x.TargetId }).ToListAsync(cancellationToken);
            keys.AddRange(found.Select(x => (x.RequestId, x.TargetId!.Value)));
        }
        // A replay must not silently select one of several receipts or reuse the
        // same import log for two different source windows.
        if (keys.GroupBy(x => x.RequestId).Any(g => g.Count() != 1) ||
            keys.GroupBy(x => x.TargetId).Any(g => g.Count() != 1))
            return ApiResponse<StudentImportJobStatusDto>.ErrorResponse(
                "Import batch receipts are ambiguous. Review committed history before resuming.", 409);
        var logIds = keys.Select(x => x.TargetId).Distinct().ToArray();
        var logs = new Dictionary<long, (Guid PublicId, int TotalRows, int SuccessRows, int FailedRows)>();
        foreach (var chunk in logIds.Chunk(400))
        {
            var found = await _logs.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ImportType == "Student" &&
                    x.CompletedAt.HasValue && chunk.Contains(x.Id))
                .Select(x => new { x.Id, x.PublicId, x.TotalRows, x.SuccessRows, x.FailedRows })
                .ToListAsync(cancellationToken);
            foreach (var log in found)
                logs.Add(log.Id, (log.PublicId, log.TotalRows, log.SuccessRows, log.FailedRows));
        }
        // The import-log counters alone are not proof that their physical audit
        // rows survived. Process one tenant-scoped aggregate per SQL-safe chunk.
        var itemCounts = new Dictionary<long, (int Rows, int Success, int WithSource)>();
        foreach (var chunk in logIds.Chunk(400))
        {
            var found = await _items.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && chunk.Contains(x.ImportLogId))
                .GroupBy(x => x.ImportLogId)
                .Select(g => new
                {
                    LogId = g.Key, Rows = g.Count(),
                    Success = g.Count(x => x.IsSuccess),
                    WithSource = g.Count(x => x.BusinessKey != null && x.BusinessKey.Trim() != "")
                }).ToListAsync(cancellationToken);
            foreach (var count in found)
                itemCounts.Add(count.LogId, (count.Rows, count.Success, count.WithSource));
        }
        var byRequest = keys.ToDictionary(x => x.RequestId, x => x.TargetId);
        var byLog = logs;
        var completed = new List<StudentImportJobBatchDto>();
        var reviewWindows = 0;
        foreach (var request in requests)
        {
            if (!byRequest.TryGetValue(request.RequestId, out var logId) ||
                !byLog.TryGetValue(logId, out var log)) continue;
            var expected = Math.Min(MaxBatchRows, totalRows - request.Offset);
            if (log.TotalRows != expected || log.SuccessRows != expected || log.FailedRows != 0)
                continue;
            var auditCode = !itemCounts.TryGetValue(logId, out var physical)
                ? "MissingLogItems"
                : physical.Rows != expected ? "RowCountMismatch"
                : physical.Success != expected ? "UnsuccessfulRows"
                : physical.WithSource != expected ? "MissingSourceKeys"
                : "Verified";
            if (auditCode != "Verified") reviewWindows++;
            completed.Add(new StudentImportJobBatchDto
            {
                RowOffset = request.Offset, ImportedRows = expected, ImportReference = log.PublicId,
                LogItemsVerified = auditCode == "Verified", LogItemAuditCode = auditCode
            });
        }
        var completeOffsets = completed.Select(x => x.RowOffset).ToHashSet();
        return ApiResponse<StudentImportJobStatusDto>.SuccessResponse(new StudentImportJobStatusDto
        {
            TotalRows = totalRows,
            CommittedRows = completed.Sum(x => x.ImportedRows),
            ReviewWindows = reviewWindows,
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
