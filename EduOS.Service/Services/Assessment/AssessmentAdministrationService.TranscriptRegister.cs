using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    // A released result must belong to the student's actual enrollment, batch, campus and academic year.
    // This predicate is shared by candidate lookup and the issuance write path.
    private IQueryable<long> EligibleTranscriptStudentIds(long tenant) =>
        from summary in _summaries.GetQueryable().AsNoTracking()
        join publication in _publications.GetQueryable().AsNoTracking()
            on new { summary.TenantId, Id = summary.ResultPublicationId }
            equals new { publication.TenantId, publication.Id }
        join enrollment in _enrollments.GetQueryable().AsNoTracking()
            on new { summary.TenantId, Id = summary.StudentEnrollmentId }
            equals new { enrollment.TenantId, enrollment.Id }
        join batch in _batches.GetQueryable().AsNoTracking()
            on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
            equals new { batch.TenantId, batch.Id }
        join assessment in _assessments.GetQueryable().AsNoTracking()
            on new { publication.TenantId, Id = publication.AssessmentId }
            equals new { assessment.TenantId, assessment.Id }
        where summary.TenantId == tenant && !summary.IsDeleted && !summary.IsWithheld &&
            !publication.IsDeleted && publication.State == ResultPublicationState.Published &&
            publication.VisibleToStudent && publication.PublishedAt != null &&
            summary.PublicationVersionNo == publication.VersionNo &&
            !enrollment.IsDeleted && !batch.IsDeleted && !assessment.IsDeleted &&
            publication.AcademicBatchId == batch.Id &&
            enrollment.CampusId == batch.CampusId &&
            assessment.CampusId == batch.CampusId &&
            assessment.AcademicYearId == enrollment.AcademicYearId
        select enrollment.StudentId;

    public async Task<ApiResponse<IReadOnlyList<TranscriptCandidateDto>>> GetTranscriptCandidatesAsync(
        string? search, CancellationToken ct = default)
    {
        var page = await SearchTranscriptCandidatesAsync(search, 1, 30, ct);
        return page.Success
            ? ApiResponse<IReadOnlyList<TranscriptCandidateDto>>.SuccessResponse(page.Data!.Items)
            : Error<IReadOnlyList<TranscriptCandidateDto>>(page.Message, page.StatusCode);
    }

    public async Task<ApiResponse<PagedResult<TranscriptCandidateDto>>> SearchTranscriptCandidatesAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<PagedResult<TranscriptCandidateDto>>("Transcript administration permission required.", 403);
        var term = search?.Trim() ?? string.Empty;
        if (term.Length is < 2 or > 100)
            return Error<PagedResult<TranscriptCandidateDto>>("Enter 2 to 100 characters of a student name or code.");
        if (page is < 1 or > 100000 || pageSize is < 1 or > 100)
            return Error<PagedResult<TranscriptCandidateDto>>("Invalid candidate pagination.");
        var tenant = _user.TenantId;
        var eligible = EligibleTranscriptStudentIds(tenant);
        var allowedStudentIds = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && AllowedCampusIds(tenant).Contains(x.CampusId))
            .Select(x => x.StudentId);
        var forbiddenStudentIds = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && !x.IsDeleted && !AllowedCampusIds(tenant).Contains(x.CampusId))
            .Select(x => x.StudentId);
        var students = _students.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && eligible.Contains(x.Id) &&
                (x.StudentCode.Contains(term) || x.FullName.Contains(term)));
        if (IsRestrictedCampusManager())
            students = students.Where(x => allowedStudentIds.Contains(x.Id) &&
                !forbiddenStudentIds.Contains(x.Id));
        var query = students
            .OrderBy(x => x.StudentCode).ThenBy(x => x.Id)
            .Select(x => new TranscriptCandidateDto
            {
                StudentReference = x.PublicId, StudentCode = x.StudentCode, StudentName = x.FullName
            });
        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TranscriptCandidateDto>>.SuccessResponse(
            new PagedResult<TranscriptCandidateDto>
            { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<TranscriptRegisterPageDto>> SearchTranscriptRegisterAsync(
        TranscriptRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<TranscriptRegisterPageDto>("Transcript administration permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.Search?.Trim().Length > 100)
            return Error<TranscriptRegisterPageDto>("Invalid transcript register filters.");
        var tenant = _user.TenantId;
        var term = request.Search?.Trim();
        var query = from issue in _transcripts.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on new { issue.TenantId, Id = issue.StudentId }
                equals new { student.TenantId, student.Id }
            where issue.TenantId == tenant && !issue.IsDeleted && !student.IsDeleted
            select new { issue, student };
        if (IsRestrictedCampusManager())
        {
            var allowedStudents = _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && AllowedCampusIds(tenant).Contains(x.CampusId))
                .Select(x => x.StudentId);
            var forbiddenStudents = _enrollments.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && !x.IsDeleted && !AllowedCampusIds(tenant).Contains(x.CampusId))
                .Select(x => x.StudentId);
            query = query.Where(x => allowedStudents.Contains(x.student.Id) &&
                !forbiddenStudents.Contains(x.student.Id));
        }
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.issue.TranscriptNumber.Contains(term) ||
                x.student.StudentCode.Contains(term) || x.student.FullName.Contains(term));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.issue.IssueDate)
            .ThenByDescending(x => x.issue.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return ApiResponse<TranscriptRegisterPageDto>.SuccessResponse(new TranscriptRegisterPageDto
        {
            TotalCount = total, Page = request.Page, PageSize = request.PageSize,
            Items = rows.Select(x => new TranscriptRegisterRowDto
            {
                Reference = x.issue.PublicId, StudentReference = x.student.PublicId,
                StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
                TranscriptNumber = x.issue.TranscriptNumber, IssueDate = x.issue.IssueDate,
                IsRevoked = x.issue.IsRevoked, RevokedAt = x.issue.RevokedAt,
                RowVersion = Version(x.issue.RowVersion)
            }).ToList()
        });
    }
}
