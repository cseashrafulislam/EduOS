using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Read-only physical integrity audit of one committed, tenant-owned import
    /// window. It never exposes source keys, global Person IDs or foreign data.
    /// </summary>
    public async Task<ApiResponse<StudentImportIntegrityReportDto>> GetIntegrityAsync(
        Guid importReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Student import access denied.", 403);
        if (importReference == Guid.Empty)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Invalid import reference.", 400);
        var tenant = _user.TenantId;
        var log = await _logs.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.PublicId == importReference &&
                x.ImportType == "Student" && !x.IsDeleted)
            .Select(x => new { x.Id, x.TotalRows, x.SuccessRows, x.FailedRows, x.CompletedAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (log == null)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import batch not found.", 404);
        if (!log.CompletedAt.HasValue || log.TotalRows is < 1 or > 100 ||
            log.SuccessRows != log.TotalRows || log.FailedRows != 0)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Only completed 1–100-row student import batches can be verified.", 409);

        var items = await _items.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.ImportLogId == log.Id && !x.IsDeleted)
            .OrderBy(x => x.RowNumber).Take(101)
            .Select(x => new { x.RowNumber, x.BusinessKey, x.IsSuccess })
            .ToListAsync(cancellationToken);
        if (items.Count > 100)
            return ApiResponse<StudentImportIntegrityReportDto>.ErrorResponse(
                "Import audit row limit exceeded.", 409);

        var sourceIds = items.Where(x => !string.IsNullOrWhiteSpace(x.BusinessKey))
            .Select(x => SourceId(x.BusinessKey!)).Distinct().ToArray();
        var keys = await _keys.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && x.Scope == SourceScope &&
                !x.IsDeleted && x.CompletedAt.HasValue && sourceIds.Contains(x.RequestId))
            .Select(x => new { x.RequestId, x.TargetId })
            .ToListAsync(cancellationToken);
        var bySource = keys.ToDictionary(x => x.RequestId, x => x.TargetId);
        var studentIds = keys.Where(x => x.TargetId.HasValue)
            .Select(x => x.TargetId!.Value).Distinct().ToArray();
        var students = await _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && studentIds.Contains(x.Id))
            .Select(x => new { x.Id, x.PersonId })
            .ToDictionaryAsync(x => x.Id, cancellationToken);
        // The global Person is read only by IDs already proven to belong to
        // this tenant's students; never enumerate platform identities.
        var personIds = students.Values.Select(x => x.PersonId).Distinct().ToArray();
        var existingPersons = (await _persons.GetQueryable().AsNoTracking()
            .Where(x => !x.IsDeleted && personIds.Contains(x.Id))
            .Select(x => x.Id).ToListAsync(cancellationToken)).ToHashSet();
        var links = await _links.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsPrimary &&
                studentIds.Contains(x.StudentId))
            .Select(x => new { x.StudentId, x.PersonId })
            .ToListAsync(cancellationToken);
        var linksByStudent = links.ToLookup(x => x.StudentId);
        var enrolled = (await _enrollments.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted &&
                studentIds.Contains(x.StudentId))
            .Select(x => x.StudentId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        var report = new StudentImportIntegrityReportDto
        {
            ImportReference = importReference,
            ExpectedRows = log.TotalRows,
            CheckedRows = items.Count
        };
        var seenStudentTargets = new HashSet<long>();
        void Issue(int row, string code, string message) =>
            report.Issues.Add(new StudentImportIntegrityIssueDto
            {
                RowNumber = row, Code = code, Message = message
            });
        if (items.Count != log.TotalRows)
            Issue(0, "LogRowCountMismatch", "Committed import row count differs from its audit log.");
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = report.Issues.Count;
            if (!item.IsSuccess || string.IsNullOrWhiteSpace(item.BusinessKey))
                Issue(item.RowNumber, "InvalidLogRow", "Import row has no valid committed source identity.");
            else if (!bySource.TryGetValue(SourceId(item.BusinessKey), out var targetId) ||
                !targetId.HasValue)
                Issue(item.RowNumber, "MissingSourceIdentity", "Committed source identity is missing.");
            else if (!students.TryGetValue(targetId.Value, out var student))
                Issue(item.RowNumber, "MissingStudent", "Tenant-owned student record is unavailable.");
            else
            {
                if (!seenStudentTargets.Add(student.Id))
                    Issue(item.RowNumber, "DuplicateStudentTarget",
                        "Multiple committed source rows resolve to one student record.");
                if (!existingPersons.Contains(student.PersonId))
                    Issue(item.RowNumber, "MissingPersonIdentity",
                        "Linked learner identity is unavailable.");
                var primaryLinks = linksByStudent[student.Id].ToArray();
                if (primaryLinks.Length != 1)
                    Issue(item.RowNumber, "AmbiguousPrimaryLink",
                        "Student must have exactly one active primary identity link.");
                if (!primaryLinks.Any(x => x.PersonId == student.PersonId))
                    Issue(item.RowNumber, "PersonLinkMismatch", "Primary learner identity link is unavailable.");
                if (!enrolled.Contains(student.Id))
                    Issue(item.RowNumber, "MissingEnrollment", "Student enrollment history is unavailable.");
            }
            if (report.Issues.Count == before) report.HealthyRows++;
        }
        return ApiResponse<StudentImportIntegrityReportDto>.SuccessResponse(report);
    }
}
