using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Exams;

public sealed partial class ExamWorkflowService
{
    // The same published-result predicate is used for candidate lookup and certificate issuance.
    // Results must be released, passed and bound to the student's actual enrollment/batch/campus/year.
    private IQueryable<long> EligibleCertificateEnrollmentIds(long tenant, DateOnly? issuedOn = null)
    {
        DateTime? publishedBefore = issuedOn.HasValue
            ? issuedOn.Value.ToDateTime(TimeOnly.MaxValue) : null;
        return (from summary in _summaries.GetQueryable().AsNoTracking()
            join publication in _publications.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.ResultPublicationId }
                equals new { publication.TenantId, publication.Id }
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            join campus in _campuses.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.CampusId }
                equals new { campus.TenantId, campus.Id }
            join assessment in _assessments.GetQueryable().AsNoTracking()
                on new { publication.TenantId, Id = publication.AssessmentId }
                equals new { assessment.TenantId, assessment.Id }
            where summary.TenantId == tenant && !summary.IsDeleted && summary.IsPassed &&
                !summary.IsWithheld && !publication.IsDeleted &&
                publication.State == ResultPublicationState.Published &&
                publication.VisibleToStudent && publication.PublishedAt != null &&
                (!publishedBefore.HasValue || publication.PublishedAt <= publishedBefore.Value) &&
                summary.PublicationVersionNo == publication.VersionNo &&
                !enrollment.IsDeleted && !batch.IsDeleted && !campus.IsDeleted &&
                !assessment.IsDeleted && assessment.State != AssessmentState.Cancelled &&
                summary.AssessmentId == assessment.Id && batch.IsActive && campus.IsActive &&
                publication.AcademicBatchId == batch.Id &&
                enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                assessment.CampusId == batch.CampusId &&
                assessment.AcademicYearId == enrollment.AcademicYearId &&
                (batch.StartDate == null || enrollment.EnrollmentDate >= batch.StartDate) &&
                (batch.EndDate == null || enrollment.EnrollmentDate <= batch.EndDate)
            select enrollment.Id).Distinct();
    }

    public async Task<ApiResponse<IReadOnlyList<CertificateCandidateDto>>> GetCertificateCandidatesAsync(
        string? search, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<IReadOnlyList<CertificateCandidateDto>>("Certificate administration permission required.", 403);
        var term = search?.Trim() ?? string.Empty;
        if (term.Length is < 2 or > 100)
            return Error<IReadOnlyList<CertificateCandidateDto>>("Enter 2 to 100 characters of a student name or code.");
        var tenant = _user.TenantId;
        var eligible = EligibleCertificateEnrollmentIds(tenant);
        IReadOnlyList<CertificateCandidateDto> rows = await (
            from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            join campus in _campuses.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.CampusId }
                equals new { campus.TenantId, campus.Id }
            where enrollment.TenantId == tenant && !enrollment.IsDeleted && !student.IsDeleted &&
                !batch.IsDeleted && !campus.IsDeleted && eligible.Contains(enrollment.Id) &&
                (student.StudentCode.Contains(term) || student.FullName.Contains(term))
            orderby student.StudentCode, enrollment.Id
            select new CertificateCandidateDto
            {
                StudentReference = student.PublicId,
                StudentEnrollmentReference = enrollment.PublicId,
                StudentCode = student.StudentCode, StudentName = student.FullName,
                BatchName = batch.Name, CampusName = campus.Name
            }).Take(30).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<CertificateCandidateDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<CertificateIssueRegisterPageDto>> SearchCertificateIssueRegisterAsync(
        CertificateIssueRegisterQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish())
            return Error<CertificateIssueRegisterPageDto>("Certificate administration permission required.", 403);
        if (request == null || request.Page is < 1 or > 100000 ||
            request.PageSize is < 1 or > 100 || request.Search?.Trim().Length > 100)
            return Error<CertificateIssueRegisterPageDto>("Invalid certificate register filters.");
        var tenant = _user.TenantId;
        var term = request.Search?.Trim();
        var query = from issue in _certificates.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking()
                on new { issue.TenantId, Id = issue.StudentId }
                equals new { student.TenantId, student.Id }
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { issue.TenantId, Id = issue.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join template in _templates.GetQueryable().AsNoTracking()
                on new { issue.TenantId, Id = issue.CertificateTemplateId }
                equals new { template.TenantId, template.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.AcademicBatchId }
                equals new { batch.TenantId, batch.Id }
            join campus in _campuses.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.CampusId }
                equals new { campus.TenantId, campus.Id }
            where issue.TenantId == tenant && !issue.IsDeleted && !student.IsDeleted &&
                !enrollment.IsDeleted && !template.IsDeleted && !batch.IsDeleted && !campus.IsDeleted &&
                enrollment.StudentId == student.Id && enrollment.CampusId == batch.CampusId
            select new { issue, student, enrollment, template, batch, campus };
        if (!string.IsNullOrWhiteSpace(term))
            query = query.Where(x => x.issue.CertificateNumber.Contains(term) ||
                x.student.StudentCode.Contains(term) || x.student.FullName.Contains(term));
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.issue.IssueDate)
            .ThenByDescending(x => x.issue.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return ApiResponse<CertificateIssueRegisterPageDto>.SuccessResponse(new CertificateIssueRegisterPageDto
        {
            TotalCount = total, Page = request.Page, PageSize = request.PageSize,
            Items = rows.Select(x => new CertificateIssueRegisterRowDto
            {
                Id = x.issue.Id, Reference = x.issue.PublicId,
                StudentReference = x.student.PublicId,
                StudentEnrollmentReference = x.enrollment.PublicId,
                CertificateNumber = x.issue.CertificateNumber,
                StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
                CampusName = x.campus.Name, BatchName = x.batch.Name,
                TemplateName = x.template.Name, IssueDate = x.issue.IssueDate,
                IsRevoked = x.issue.IsRevoked, RevokedAt = x.issue.RevokedAt,
                RowVersion = Version(x.issue.RowVersion)
            }).ToList()
        });
    }
}
