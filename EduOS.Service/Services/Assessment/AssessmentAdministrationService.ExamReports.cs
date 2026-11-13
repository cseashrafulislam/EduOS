using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService
{
    public async Task<ApiResponse<PagedResult<ExamReportScopeDto>>> SearchExamReportScopesAsync(
        string? search, int page, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<ExamReportScopeDto>>("Examination reporting permission required.", 403);
        if (page is < 1 or > 100000 || search?.Trim().Length > 100)
            return Error<PagedResult<ExamReportScopeDto>>("Invalid report scope search.");
        var tenant = _user.TenantId;
        var q = from pub in _publications.GetQueryable().AsNoTracking()
            join exam in _assessments.GetQueryable().AsNoTracking()
                on new { pub.TenantId, Id = pub.AssessmentId } equals new { exam.TenantId, exam.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { pub.TenantId, Id = pub.AcademicBatchId } equals new { batch.TenantId, Id = (long?)batch.Id }
            where pub.TenantId == tenant && !pub.IsDeleted && !exam.IsDeleted && !batch.IsDeleted &&
                (pub.State == ResultPublicationState.Published || pub.State == ResultPublicationState.Withdrawn) &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId
            select new { pub.AssessmentId, pub.AcademicBatchId, exam.CampusId,
                AssessmentName = exam.Name, BatchName = batch.Name };
        if (IsRestrictedCampusManager())
            q = q.Where(x => AllowedCampusIds(tenant).Contains(x.CampusId));
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.AssessmentName.StartsWith(term) || x.BatchName.StartsWith(term));
        var groups = q.Select(x => new ExamReportScopeDto
        {
            AssessmentId = x.AssessmentId, AcademicBatchId = x.AcademicBatchId,
            AssessmentName = x.AssessmentName, AcademicBatchName = x.BatchName
        }).Distinct();
        var total = await groups.CountAsync(ct);
        var skip = ((long)page - 1) * 30;
        if (skip > int.MaxValue) return Error<PagedResult<ExamReportScopeDto>>("Report scope page exceeds supported range.");
        var items = await groups.OrderBy(x => x.AssessmentName).ThenBy(x => x.AcademicBatchName)
            .ThenBy(x => x.AssessmentId).ThenBy(x => x.AcademicBatchId)
            .Skip((int)skip).Take(30).ToListAsync(ct);
        return ApiResponse<PagedResult<ExamReportScopeDto>>.SuccessResponse(new()
        {
            Items = items, TotalCount = total, Page = page, PageSize = 30
        });
    }

    public async Task<ApiResponse<PagedResult<ExamReportSubjectDto>>> SearchExamReportSubjectsAsync(
        long publicationId, string? search, int page, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<PagedResult<ExamReportSubjectDto>>("Examination reporting permission required.", 403);
        if (publicationId <= 0 || page is < 1 or > 100000 || search?.Trim().Length > 100)
            return Error<PagedResult<ExamReportSubjectDto>>("Invalid subject search.");
        var tenant = _user.TenantId;
        var scope = await (from pub in _publications.GetQueryable().AsNoTracking()
            join exam in _assessments.GetQueryable().AsNoTracking()
                on new { pub.TenantId, Id = pub.AssessmentId } equals new { exam.TenantId, exam.Id }
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { pub.TenantId, Id = pub.AcademicBatchId } equals new { batch.TenantId, Id = (long?)batch.Id }
            where pub.TenantId == tenant && pub.Id == publicationId && !pub.IsDeleted &&
                !exam.IsDeleted && !batch.IsDeleted &&
                (pub.State == ResultPublicationState.Published || pub.State == ResultPublicationState.Withdrawn) &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId
            select new { pub.AssessmentId, pub.AcademicBatchId, exam.CampusId }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<PagedResult<ExamReportSubjectDto>>("Finalized publication not found.", 404);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<PagedResult<ExamReportSubjectDto>>("Campus access denied.", 403);

        var q = from assessmentSubject in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on new { assessmentSubject.TenantId, Id = assessmentSubject.SubjectOfferingId }
                equals new { offering.TenantId, offering.Id }
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on new { offering.TenantId, Id = offering.CurriculumSubjectId }
                equals new { curriculum.TenantId, curriculum.Id }
            join subject in _subjectNames.GetQueryable().AsNoTracking()
                on new { curriculum.TenantId, Id = curriculum.SubjectId }
                equals new { subject.TenantId, subject.Id }
            where assessmentSubject.TenantId == tenant &&
                assessmentSubject.AssessmentId == scope.AssessmentId &&
                offering.AcademicBatchId == scope.AcademicBatchId &&
                !assessmentSubject.IsDeleted && !offering.IsDeleted &&
                !curriculum.IsDeleted && !subject.IsDeleted
            select new { assessmentSubject.Id, subject.Name, subject.Code };
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.Name.StartsWith(term) || x.Code.StartsWith(term));
        var count = await q.CountAsync(ct);
        var skip = ((long)page - 1) * 30;
        if (skip > int.MaxValue) return Error<PagedResult<ExamReportSubjectDto>>("Subject page exceeds supported range.");
        var items = await q.OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((int)skip).Take(30)
            .Select(x => new ExamReportSubjectDto
            {
                AssessmentSubjectId = x.Id, SubjectName = x.Name, SubjectCode = x.Code
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<ExamReportSubjectDto>>.SuccessResponse(new()
        {
            Page = page, PageSize = 30, TotalCount = count, Items = items
        });
    }

    /// <summary>
    /// Read-only, version-pinned exam reports. All filtering/paging occurs in SQL;
    /// the client cannot supply tenant/campus or override a published grade.
    /// </summary>
    public async Task<ApiResponse<ExamReportPageDto>> SearchExamReportAsync(
        ExamReportQueryDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ExamReportPageDto>("Examination reporting permission required.", 403);
        if (request == null || request.PublicationId <= 0 ||
            !Enum.IsDefined(typeof(ExamReportKind), request.Kind) ||
            request.Page is < 1 or > 100000 || request.PageSize is < 1 or > 50 ||
            request.Search?.Trim().Length > 100 || request.AssessmentSubjectId is <= 0)
            return Error<ExamReportPageDto>("Invalid examination report filters.");

        if (request.Kind == ExamReportKind.AcademicTranscript)
            return Error<ExamReportPageDto>("Formal academic transcripts are issued through the existing Transcript Issue Register.", 409);
        if (request.Kind == ExamReportKind.PracticalSubjectFail)
            return Error<ExamReportPageDto>(
                "A historical practical-component failure report requires immutable per-component publication evidence. Current course snapshots cannot prove practical-only failure.", 409);

        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == request.PublicationId && !x.IsDeleted, ct);
        if (publication == null) return Error<ExamReportPageDto>("Publication not found.", 404);
        if (publication.State is not (ResultPublicationState.Published or ResultPublicationState.Withdrawn))
            return Error<ExamReportPageDto>("Only finalized result versions can be reported.", 409);
        var scope = await (from exam in _assessments.GetQueryable().AsNoTracking()
            join batch in _batches.GetQueryable().AsNoTracking()
                on new { exam.TenantId, CampusId = exam.CampusId, YearId = exam.AcademicYearId }
                equals new { batch.TenantId, CampusId = batch.CampusId, YearId = batch.AcademicYearId }
            where exam.TenantId == tenant && exam.Id == publication.AssessmentId &&
                batch.Id == publication.AcademicBatchId && !exam.IsDeleted && !batch.IsDeleted
            select new { exam.CampusId, exam.AcademicYearId,
                AssessmentName = exam.Name, BatchName = batch.Name }).FirstOrDefaultAsync(ct);
        if (scope == null) return Error<ExamReportPageDto>("Publication scope needs reconciliation.", 409);
        if (!await CanAccessCampusAsync(scope.CampusId, ct))
            return Error<ExamReportPageDto>("Campus access denied.", 403);

        // A corrupted row must never disappear behind filters, a page boundary or a group.
        var proofs = await GetPublicationEvidenceAsync([publication.Id], scope.CampusId,
            scope.AcademicYearId, ct);
        if (!proofs.TryGetValue(publication.Id, out var proof) || !proof.IsReconciled)
            return Error<ExamReportPageDto>("Publication snapshot requires reconciliation.", 409);

        var courseReport = request.Kind is ExamReportKind.SubjectWisePosition or
            ExamReportKind.SubjectWiseFail or ExamReportKind.SubjectWisePass or
            ExamReportKind.OneSubjectFail or ExamReportKind.TwoSubjectFail or
            ExamReportKind.ThreePlusSubjectFail or ExamReportKind.StudentWiseSubject or
            ExamReportKind.SubjectMarkSheetBlank;
        if (courseReport && (!publication.HasCourseGradeSnapshots || _courseSnapshots == null))
            return Error<ExamReportPageDto>(
                "This publication predates immutable subject-level results; subject reports are unavailable.", 409);

        var q = from summary in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on new { summary.TenantId, Id = summary.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in _students.GetQueryable().AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where summary.TenantId == tenant && !summary.IsDeleted && !enrollment.IsDeleted &&
                !student.IsDeleted && summary.ResultPublicationId == publication.Id &&
                summary.AssessmentId == publication.AssessmentId &&
                summary.PublicationVersionNo == publication.VersionNo &&
                enrollment.AcademicBatchId == publication.AcademicBatchId &&
                enrollment.AcademicYearId == scope.AcademicYearId &&
                enrollment.CampusId == scope.CampusId
            select new { summary, enrollment, student };

        // Publication proof also checks row provenance; guard joins independently against
        // future schema/query changes and fail before search can hide malformed rows.
        var snapshotCount = await _summaries.GetQueryable().AsNoTracking().CountAsync(x =>
            x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted, ct);
        if (await q.CountAsync(ct) != snapshotCount)
            return Error<ExamReportPageDto>("Published enrollment provenance requires reconciliation.", 409);

        var unfilteredRows = q;
        var term = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.enrollment.RollNo.StartsWith(term) ||
                x.student.StudentCode.StartsWith(term) || x.student.FullName.StartsWith(term));

        var skip = ((long)request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue) return Error<ExamReportPageDto>("Report page exceeds supported range.");
        var response = new ExamReportPageDto
        {
            PublicationId = publication.Id, VersionNo = publication.VersionNo,
            Kind = request.Kind, AssessmentName = scope.AssessmentName,
            AcademicBatchName = scope.BatchName,
            IsWithdrawn = publication.State == ResultPublicationState.Withdrawn,
            Page = request.Page, PageSize = request.PageSize
        };

        if (request.Kind == ExamReportKind.GroupWiseSummary)
        {
            var groups = q.GroupBy(x => x.enrollment.AcademicTrackId)
                .Select(g => new
                {
                    TrackId = g.Key, Students = g.Count(),
                    Passed = g.Count(x => x.summary.IsPassed && !x.summary.IsWithheld),
                    Failed = g.Count(x => !x.summary.IsPassed && !x.summary.IsWithheld),
                    Withheld = g.Count(x => x.summary.IsWithheld)
                });
            response.TotalCount = await groups.CountAsync(ct);
            var page = await groups.OrderBy(x => x.TrackId).Skip((int)skip)
                .Take(request.PageSize).ToListAsync(ct);
            var ids = page.Where(x => x.TrackId.HasValue).Select(x => x.TrackId!.Value).ToArray();
            var names = _academicTracks == null ? new Dictionary<long, string>() :
                await _academicTracks.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && ids.Contains(x.Id) && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
            response.Items = page.Select(x => new ExamReportRowDto
            {
                GroupName = x.TrackId.HasValue
                    ? names.GetValueOrDefault(x.TrackId.Value, "Track #" + x.TrackId.Value)
                    : "Unassigned", StudentCount = x.Students, PassedCount = x.Passed,
                FailedCount = x.Failed, WithheldCount = x.Withheld
            }).ToArray();
            return ApiResponse<ExamReportPageDto>.SuccessResponse(response);
        }

        if (request.Kind is ExamReportKind.OneSubjectFail or ExamReportKind.TwoSubjectFail or
            ExamReportKind.ThreePlusSubjectFail)
        {
            q = q.Where(x => !x.summary.IsPassed && !x.summary.IsWithheld);
            var failures = _courseSnapshots!.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted)
                .GroupBy(x => x.StudentEnrollmentId).Select(g => new
                {
                    EnrollmentId = g.Key,
                    Failed = g.Count(x => !x.IsPassed && !x.IsWithheld),
                    Withheld = g.Count(x => x.IsWithheld)
                });
            q = from row in q
                join count in failures on row.summary.StudentEnrollmentId equals count.EnrollmentId
                where count.Withheld == 0 &&
                    (request.Kind == ExamReportKind.OneSubjectFail && count.Failed == 1 ||
                     request.Kind == ExamReportKind.TwoSubjectFail && count.Failed == 2 ||
                     request.Kind == ExamReportKind.ThreePlusSubjectFail && count.Failed >= 3)
                select row;
            response.TotalCount = await q.CountAsync(ct);
            var page = await q.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.summary.Id)
                .Skip((int)skip).Take(request.PageSize)
                .Select(x => new { x.summary.StudentEnrollmentId, x.student.PublicId,
                    x.enrollment.RollNo, x.student.StudentCode, x.student.FullName,
                    x.summary.GPA, x.summary.GradeLetter }).ToListAsync(ct);
            var pageIds = page.Select(x => x.StudentEnrollmentId).ToArray();
            var failCounts = await failures.Where(x => pageIds.Contains(x.EnrollmentId))
                .ToDictionaryAsync(x => x.EnrollmentId, x => x.Failed, ct);
            response.Items = page.Select(x => new ExamReportRowDto
            {
                StudentReference = x.PublicId, RollNo = x.RollNo,
                StudentCode = x.StudentCode, StudentName = x.FullName,
                Gpa = x.GPA, GradeLetter = x.GradeLetter,
                FailedSubjects = failCounts.GetValueOrDefault(x.StudentEnrollmentId)
            }).ToArray();
            return ApiResponse<ExamReportPageDto>.SuccessResponse(response);
        }

        if (request.Kind is ExamReportKind.SubjectWisePosition or ExamReportKind.SubjectWiseFail or
            ExamReportKind.SubjectWisePass or ExamReportKind.StudentWiseSubject or
            ExamReportKind.SubjectMarkSheetBlank)
        {
            if ((request.Kind is ExamReportKind.SubjectWisePosition or ExamReportKind.SubjectWiseFail or
                ExamReportKind.SubjectWisePass or ExamReportKind.SubjectMarkSheetBlank) &&
                !request.AssessmentSubjectId.HasValue)
                return Error<ExamReportPageDto>("Select a subject for this report.");

            var courses = from course in _courseSnapshots!.GetQueryable().AsNoTracking()
                join row in unfilteredRows on course.StudentEnrollmentId equals row.summary.StudentEnrollmentId
                join assessmentSubject in _subjects.GetQueryable().AsNoTracking()
                    on new { course.TenantId, Id = course.AssessmentSubjectId }
                    equals new { assessmentSubject.TenantId, assessmentSubject.Id }
                join offering in _offerings.GetQueryable().AsNoTracking()
                    on new { assessmentSubject.TenantId, Id = assessmentSubject.SubjectOfferingId }
                    equals new { offering.TenantId, offering.Id }
                join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                    on new { course.TenantId, Id = course.CurriculumSubjectId }
                    equals new { curriculum.TenantId, curriculum.Id }
                join subject in _subjectNames.GetQueryable().AsNoTracking()
                    on new { curriculum.TenantId, Id = curriculum.SubjectId }
                    equals new { subject.TenantId, subject.Id }
                where course.TenantId == tenant && !course.IsDeleted &&
                    course.ResultPublicationId == publication.Id && !assessmentSubject.IsDeleted &&
                    !offering.IsDeleted && !curriculum.IsDeleted && !subject.IsDeleted &&
                    assessmentSubject.AssessmentId == publication.AssessmentId &&
                    offering.AcademicBatchId == publication.AcademicBatchId &&
                    offering.CurriculumSubjectId == course.CurriculumSubjectId
                select new { course, row, SubjectName = subject.Name, SubjectCode = subject.Code };
            // Check the whole version's course/catalog provenance before any filter
            // so a deleted/mislinked offering cannot silently disappear from a report.
            var allCourses = await _courseSnapshots.GetQueryable().AsNoTracking().CountAsync(x =>
                x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted, ct);
            if (await courses.CountAsync(ct) != allCourses)
                return Error<ExamReportPageDto>("Published subject catalogue provenance requires reconciliation.", 409);
            if (request.AssessmentSubjectId.HasValue)
                courses = courses.Where(x => x.course.AssessmentSubjectId == request.AssessmentSubjectId.Value);
            if (!string.IsNullOrWhiteSpace(term))
                courses = courses.Where(x => x.row.enrollment.RollNo.StartsWith(term) ||
                    x.row.student.StudentCode.StartsWith(term) || x.row.student.FullName.StartsWith(term));
            if (request.Kind == ExamReportKind.SubjectWiseFail)
                courses = courses.Where(x => !x.course.IsPassed && !x.course.IsWithheld);
            if (request.Kind == ExamReportKind.SubjectWisePass)
                courses = courses.Where(x => x.course.IsPassed && !x.course.IsWithheld);
            response.TotalCount = await courses.CountAsync(ct);
            var sorted = request.Kind == ExamReportKind.SubjectWisePosition
                ? courses.OrderBy(x => x.course.IsWithheld).ThenByDescending(x => x.course.ObtainedMarks)
                    .ThenBy(x => x.row.enrollment.RollNo).ThenBy(x => x.course.Id)
                : courses.OrderBy(x => x.row.enrollment.RollNo).ThenBy(x => x.SubjectName)
                    .ThenBy(x => x.course.Id);
            var rankCourses = _courseSnapshots.GetQueryable().AsNoTracking();
            var items = await sorted.Skip((int)skip).Take(request.PageSize)
                .Select(x => new ExamReportRowDto
                {
                    StudentReference = x.row.student.PublicId,
                    RollNo = x.row.enrollment.RollNo,
                    StudentCode = x.row.student.StudentCode,
                    StudentName = x.row.student.FullName,
                    SubjectName = x.SubjectName, SubjectCode = x.SubjectCode,
                    ObtainedMarks = request.Kind == ExamReportKind.SubjectMarkSheetBlank
                        ? null : x.course.ObtainedMarks,
                    TotalMarks = x.course.FullMarks,
                    Gpa = request.Kind == ExamReportKind.SubjectMarkSheetBlank
                        ? null : x.course.GradePoint,
                    GradeLetter = request.Kind == ExamReportKind.SubjectMarkSheetBlank
                        ? null : x.course.GradeLetter,
                    IsPassed = x.course.IsPassed, IsWithheld = x.course.IsWithheld,
                    SubjectPosition = request.Kind == ExamReportKind.SubjectWisePosition &&
                        !x.course.IsWithheld ? rankCourses.Count(other =>
                            other.TenantId == tenant && other.ResultPublicationId == publication.Id &&
                            other.AssessmentSubjectId == x.course.AssessmentSubjectId && !other.IsDeleted &&
                            !other.IsWithheld && other.ObtainedMarks > x.course.ObtainedMarks) + 1 : null
                }).ToListAsync(ct);
            response.Items = items;
            return ApiResponse<ExamReportPageDto>.SuccessResponse(response);
        }

        q = request.Kind switch
        {
            ExamReportKind.Counselling =>
                q.Where(x => !x.summary.IsPassed || x.summary.IsWithheld),
            ExamReportKind.RollWiseFail =>
                q.Where(x => !x.summary.IsPassed && !x.summary.IsWithheld),
            ExamReportKind.PassList => q.Where(x => x.summary.IsPassed && !x.summary.IsWithheld),
            ExamReportKind.Top20Positions => q.Where(x => x.summary.MeritPosition.HasValue &&
                x.summary.MeritPosition <= 20 && !x.summary.IsWithheld),
            _ => q
        };
        response.TotalCount = await q.CountAsync(ct);
        var ordered = request.Kind switch
        {
            ExamReportKind.Top20Positions or ExamReportKind.Tabulation =>
                q.OrderBy(x => x.summary.MeritPosition == null)
                    .ThenBy(x => x.summary.MeritPosition).ThenBy(x => x.enrollment.RollNo),
            ExamReportKind.GpaWiseResults => q.OrderBy(x => x.summary.GPA == null)
                .ThenByDescending(x => x.summary.GPA).ThenBy(x => x.enrollment.RollNo),
            _ => q.OrderBy(x => x.enrollment.RollNo).ThenBy(x => x.summary.Id)
        };
        response.Items = await ordered.Skip((int)skip).Take(request.PageSize)
            .Select(x => new ExamReportRowDto
            {
                StudentReference = x.student.PublicId, RollNo = x.enrollment.RollNo,
                StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
                TotalMarks = x.summary.TotalMarks, ObtainedMarks = x.summary.ObtainedMarks,
                Percentage = x.summary.Percentage, Gpa = x.summary.GPA,
                Cgpa = x.summary.CGPA, GradeLetter = x.summary.GradeLetter,
                MeritPosition = x.summary.MeritPosition,
                IsPassed = x.summary.IsPassed, IsWithheld = x.summary.IsWithheld
            }).ToListAsync(ct);
        return ApiResponse<ExamReportPageDto>.SuccessResponse(response);
    }
}
