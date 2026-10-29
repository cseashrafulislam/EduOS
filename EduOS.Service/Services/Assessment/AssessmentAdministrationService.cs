using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;

namespace EduOS.Service.Services.AssessmentAdministration;

public sealed partial class AssessmentAdministrationService : IAssessmentAdministrationService
{
    private readonly IGenericRepository<Assessment> _assessments;
    private readonly IGenericRepository<AssessmentAttemptRegistration>? _attemptRegistrations;
    private readonly IGenericRepository<AssessmentSubject> _subjects;
    private readonly IGenericRepository<AssessmentMarkReviewEvent> _markReviews;
    private readonly IGenericRepository<ResultRescrutinyEvent> _rescrutinyEvents;
    private readonly IGenericRepository<ResultCorrectionEvent>? _corrections;
    private readonly IGenericRepository<StudentResultCourseSnapshot>? _courseSnapshots;
    private readonly IGenericRepository<AssessmentSchedule> _schedules;
    private readonly IGenericRepository<StudentAssessmentMark> _marks;
    private readonly IGenericRepository<ResultPublication> _publications;
    private readonly IGenericRepository<StudentResultSummary> _summaries;
    private readonly IGenericRepository<GradeScheme> _gradeSchemes;
    private readonly IGenericRepository<GradeRule> _grades;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Subject> _subjectNames;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<UserCampusAccess> _campusAccesses;
    private readonly IGenericRepository<Room> _rooms;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian>? _rescrutinyGuardians;
    private readonly IGenericRepository<StudentGuardian>? _rescrutinyStudentGuardians;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IGenericRepository<AssessmentComponent> _components;
    private readonly IGenericRepository<StudentAssessmentComponentMark> _componentMarks;
    private readonly IGenericRepository<CertificateTemplate> _templates;
    private readonly IGenericRepository<CertificateIssue> _certificates;
    private readonly IGenericRepository<TranscriptIssue> _transcripts;
    private readonly IGenericRepository<AuditLog> _auditLogs;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AssessmentAdministrationService> _logger;
    private readonly IDataProtector? _transcriptProtector;
    private readonly IDataProtector? _certificateProtector;
    private readonly IGenericRepository<Tenant>? _issuingTenants;

    public AssessmentAdministrationService(IGenericRepository<Assessment> assessments,
        IGenericRepository<AssessmentSubject> subjects,
        IGenericRepository<AssessmentMarkReviewEvent> markReviews, IGenericRepository<AssessmentSchedule> schedules,
        IGenericRepository<StudentAssessmentMark> marks, IGenericRepository<ResultPublication> publications,
        IGenericRepository<StudentResultSummary> summaries, IGenericRepository<GradeScheme> gradeSchemes,
        IGenericRepository<GradeRule> grades, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Subject> subjectNames, IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<SubjectOffering> offerings, IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms, IGenericRepository<Campus> campuses,
        IGenericRepository<UserCampusAccess> campusAccesses,
        IGenericRepository<Room> rooms, IGenericRepository<Student> students,
        IGenericRepository<Employee> employees, IGenericRepository<InstructorAssignment> instructors,
        IGenericRepository<AssessmentComponent> components,
        IGenericRepository<StudentAssessmentComponentMark> componentMarks,
        IGenericRepository<CertificateTemplate> templates, IGenericRepository<CertificateIssue> certificates,
        IGenericRepository<TranscriptIssue> transcripts, IGenericRepository<AuditLog> auditLogs,
        IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<AssessmentAdministrationService> logger,
        IGenericRepository<ResultRescrutinyEvent> rescrutinyEvents,
        IGenericRepository<Guardian>? rescrutinyGuardians = null,
        IGenericRepository<StudentGuardian>? rescrutinyStudentGuardians = null,
        IGenericRepository<ResultCorrectionEvent>? corrections = null,
        IGenericRepository<StudentResultCourseSnapshot>? courseSnapshots = null,
        IDataProtectionProvider? transcriptProtection = null,
        IGenericRepository<Tenant>? issuingTenants = null,
        IGenericRepository<AssessmentAttemptRegistration>? attemptRegistrations = null)
    {
        _assessments = assessments; _subjects = subjects; _markReviews = markReviews;
        _rescrutinyEvents = rescrutinyEvents;
        _corrections = corrections;
        _courseSnapshots = courseSnapshots;
        _rescrutinyGuardians = rescrutinyGuardians;
        _rescrutinyStudentGuardians = rescrutinyStudentGuardians;
        _schedules = schedules;
        _marks = marks; _publications = publications; _summaries = summaries;
        _gradeSchemes = gradeSchemes; _grades = grades; _enrollments = enrollments;
        _subjectNames = subjectNames; _registrations = registrations; _offerings = offerings;
        _curriculumSubjects = curriculumSubjects; _batches = batches; _years = years;
        _terms = terms; _campuses = campuses; _campusAccesses = campusAccesses; _rooms = rooms; _students = students;
        _employees = employees; _instructors = instructors; _components = components;
        _componentMarks = componentMarks; _templates = templates;
        _certificates = certificates; _transcripts = transcripts; _auditLogs = auditLogs;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
        _transcriptProtector = transcriptProtection?.CreateProtector(TranscriptSnapshotIntegrity.Purpose);
        _certificateProtector = transcriptProtection?.CreateProtector(CertificateSnapshotIntegrity.Purpose);
        _issuingTenants = issuingTenants;
        _attemptRegistrations = attemptRegistrations;
    }

    // Legacy bounded selector remains available to existing clients.
    public async Task<ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>> GetAvailableScopesAsync(
        CancellationToken ct = default)
    {
        var result = await GetScopePageAsync(null, 1, 500, ct);
        return !result.Success || result.Data == null
            ? Error<IReadOnlyList<AssessmentScopeOptionDto>>(result.Message, result.StatusCode)
            : ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>.SuccessResponse(result.Data.Items);
    }

    public Task<ApiResponse<PagedResult<AssessmentScopeOptionDto>>> SearchAvailableScopesAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 || search?.Length > 100)
            return Task.FromResult(Error<PagedResult<AssessmentScopeOptionDto>>("Invalid assessment search or page."));
        return GetScopePageAsync(search, page, pageSize, ct);
    }

    private async Task<ApiResponse<PagedResult<AssessmentScopeOptionDto>>> GetScopePageAsync(
        string? search, int page, int pageSize, CancellationToken ct)
    {
        if (!CanRead()) return Error<PagedResult<AssessmentScopeOptionDto>>("Assessment permission required.", 403);
        var tenant = _user.TenantId;
        var q = from exam in _assessments.GetQueryable().AsNoTracking()
            join sub in _subjects.GetQueryable().AsNoTracking() on exam.Id equals sub.AssessmentId
            join offering in _offerings.GetQueryable().AsNoTracking() on sub.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking() on offering.AcademicBatchId equals batch.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking() on offering.CurriculumSubjectId equals item.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where exam.TenantId == tenant && sub.TenantId == tenant && offering.TenantId == tenant &&
                batch.TenantId == tenant && item.TenantId == tenant && subject.TenantId == tenant &&
                exam.CampusId == batch.CampusId && exam.AcademicYearId == batch.AcademicYearId &&
                exam.State != AssessmentState.Cancelled && batch.IsActive && offering.IsActive &&
                !exam.IsDeleted && !sub.IsDeleted && !offering.IsDeleted && !batch.IsDeleted &&
                !item.IsDeleted && !subject.IsDeleted
            select new { exam, sub, offering, batch, subject };
        if (IsRestrictedCampusManager())
            q = q.Where(x => AllowedCampusIds(tenant).Contains(x.exam.CampusId));
        if (!CanPublish())
        {
            var employeeId = await OwnTeacherIdAsync(ct);
            if (employeeId == 0)
                return ApiResponse<PagedResult<AssessmentScopeOptionDto>>.SuccessResponse(
                    new PagedResult<AssessmentScopeOptionDto>
                    { Items = [], TotalCount = 0, Page = page, PageSize = pageSize });
            var allowed = _instructors.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EmployeeId == employeeId && x.IsActive && !x.IsDeleted)
                .Select(x => x.SubjectOfferingId);
            q = q.Where(x => allowed.Contains(x.offering.Id));
        }
        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.exam.Name.StartsWith(term) ||
                x.batch.Name.StartsWith(term) || x.subject.Name.StartsWith(term));
        var total = await q.CountAsync(ct);
        var skip = ((long)page - 1) * pageSize;
        if (skip > int.MaxValue)
            return Error<PagedResult<AssessmentScopeOptionDto>>("Assessment page exceeds supported range.");
        var rows = await q.OrderByDescending(x => x.exam.StartDate)
            .ThenBy(x => x.exam.Id).ThenBy(x => x.batch.Id).ThenBy(x => x.sub.Id)
            .Skip((int)skip).Take(pageSize).Select(x => new AssessmentScopeOptionDto
            {
                AssessmentId = x.exam.Id, AssessmentReference = x.exam.PublicId,
                AssessmentName = x.exam.Name,
                AcademicYearId = x.exam.AcademicYearId, AcademicBatchId = x.batch.Id,
                AcademicBatchName = x.batch.Name, AssessmentSubjectId = x.sub.Id,
                SubjectName = x.subject.Name, State = x.exam.State
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<AssessmentScopeOptionDto>>.SuccessResponse(
            new PagedResult<AssessmentScopeOptionDto>
            { Items = rows, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<AssessmentMarkRosterDto>> GetMarkRosterAsync(
        AssessmentMarkRosterQueryDto request, CancellationToken ct = default)
    {
        if (!CanRead()) return Error<AssessmentMarkRosterDto>("Assessment permission required.", 403);
        var scope = await ScopeAsync(request, ct);
        if (scope == null) return Error<AssessmentMarkRosterDto>("Assessment or batch not found.", 404);
        if (request.AssessmentSubjectId <= 0)
            return Error<AssessmentMarkRosterDto>("Assessment subject is required.");
        var sub = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == request.AssessmentSubjectId &&
            x.AssessmentId == scope.Value.Exam.Id && !x.IsDeleted, ct);
        if (sub == null) return Error<AssessmentMarkRosterDto>("Assessment subject not found.", 404);
        var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == sub.SubjectOfferingId &&
            x.AcademicBatchId == request.AcademicBatchId && x.IsActive && !x.IsDeleted, ct);
        if (offering == null) return Error<AssessmentMarkRosterDto>("Subject offering does not belong to this batch.", 409);
        if (!await CanEditOfferingAsync(offering.Id, ct))
            return Error<AssessmentMarkRosterDto>("Instructor is not assigned to this offering.", 403);
        var eligible = await EligibleAsync(offering.Id, request.AcademicBatchId, ct);
        if (scope.Value.Exam.AttemptMode != AssessmentAttemptMode.Regular)
        {
            if (_attemptRegistrations == null)
                return Error<AssessmentMarkRosterDto>("Attempt register is unavailable.", 503);
            var approved = await ApprovedAttemptQuery(sub.Id).Select(x => x.StudentSubjectRegistrationId).ToListAsync(ct);
            eligible = eligible.Where(x => approved.Contains(x.RegistrationId)).ToList();
        }
        if (eligible.Count > 2000)
            return Error<AssessmentMarkRosterDto>("Assessment roster exceeds synchronous reporting limit.", 409);
        var ids = eligible.Select(x => x.RegistrationId).ToArray();
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.AssessmentSubjectId == sub.Id &&
            ids.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.StudentSubjectRegistrationId, ct);
        var name = await (from item in _curriculumSubjects.GetQueryable().AsNoTracking()
            join subject in _subjectNames.GetQueryable().AsNoTracking() on item.SubjectId equals subject.Id
            where item.TenantId == _user.TenantId && subject.TenantId == _user.TenantId &&
                item.Id == offering.CurriculumSubjectId
            select subject.Name).FirstOrDefaultAsync(ct);
        var published = await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.AssessmentId == scope.Value.Exam.Id &&
            x.AcademicBatchId == request.AcademicBatchId && x.State == ResultPublicationState.Published &&
            !x.IsDeleted, ct);
        return ApiResponse<AssessmentMarkRosterDto>.SuccessResponse(new AssessmentMarkRosterDto
        {
            AssessmentId = scope.Value.Exam.Id, AssessmentName = scope.Value.Exam.Name,
            AcademicBatchId = request.AcademicBatchId, AssessmentSubjectId = sub.Id,
            SubjectName = name ?? string.Empty, FullMarks = sub.FullMarks,
            PassMarks = sub.PassMarks, IsResultPublished = published,
            Students = eligible.Select(x =>
            {
                marks.TryGetValue(x.RegistrationId, out var row);
                return new AssessmentMarkRosterItemDto
                {
                    StudentReference = x.StudentReference, StudentCode = x.StudentCode,
                    StudentName = x.StudentName, RollNo = x.Roll,
                    StudentSubjectRegistrationId = x.RegistrationId,
                    ObtainedMarks = row?.ObtainedMarks, IsAbsent = row?.IsAbsent ?? false,
                    IsWithheld = row?.IsWithheld ?? false, GradeLetter = row?.GradeLetter,
                    GradePoint = row?.GradePoint, RowVersion = row == null ? null : Version(row.RowVersion)
                };
            }).ToList()
        });
    }

    public async Task<ApiResponse<bool>> SaveMarksRegisterAsync(SaveMarksRegisterRequestDto request,
        CancellationToken ct = default)
    {
        if (!CanRead()) return Error<bool>("Assessment permission required.", 403);
        if (request == null || request.AssessmentSubjectId <= 0 || request.Marks == null ||
            request.Marks.Count is < 1 or > 2000 ||
            request.Marks.Any(x => x == null || x.StudentSubjectRegistrationId <= 0 ||
                x.ObtainedMarks < 0m || x.Remarks?.Length > 1000) ||
            request.Marks.Select(x => x.StudentSubjectRegistrationId).Distinct().Count() != request.Marks.Count)
            return Error<bool>("Invalid marks register or duplicate student registration.");
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var tenant = _user.TenantId;
                var sub = await _subjects.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, token);
                if (sub == null) return Error<bool>("Assessment subject not found.", 404);
                var exam = await _assessments.GetQueryable().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == sub.AssessmentId && !x.IsDeleted, token);
                if (exam != null && !await CanAccessCampusAsync(exam.CampusId, token))
                    return Error<bool>("Campus access denied.", 403);
                if (exam == null || exam.State is not (AssessmentState.MarksEntry or AssessmentState.InProgress))
                    return Error<bool>("Assessment is not open for marks entry.", 409);
                if (!await CanEditOfferingAsync(sub.SubjectOfferingId, token))
                    return Error<bool>("Instructor assignment not found.", 403);
                var review = await _markReviews.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenant && x.AssessmentSubjectId == sub.Id && !x.IsDeleted)
                    .OrderByDescending(x => x.Id).Select(x => (AssessmentMarkReviewDecision?)x.Decision)
                    .FirstOrDefaultAsync(token);
                if (review is AssessmentMarkReviewDecision.Submitted or AssessmentMarkReviewDecision.Approved)
                    return Error<bool>("Review decision prevents marks editing.", 409);
                var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == sub.SubjectOfferingId && x.IsActive, token);
                if (offering == null) return Error<bool>("Subject offering inactive.", 409);
                if (await HasFinalizedPublicationAsync(exam.Id, offering.AcademicBatchId, token))
                    return Error<bool>(
                        "Published or withdrawn results require an approved, audited amendment.", 409);
                // Validate only submitted registrations in SQL, not the legacy 2,001-row roster.
                var ids = request.Marks.Select(x => x.StudentSubjectRegistrationId).ToArray();
                var eligibleIds = await (from registration in _registrations.GetQueryable().AsNoTracking()
                    join enrollment in _enrollments.GetQueryable().AsNoTracking()
                        on registration.StudentEnrollmentId equals enrollment.Id
                    join student in _students.GetQueryable().AsNoTracking()
                        on enrollment.StudentId equals student.Id
                    where registration.TenantId == tenant && enrollment.TenantId == tenant &&
                        student.TenantId == tenant && ids.Contains(registration.Id) &&
                        registration.SubjectOfferingId == offering.Id &&
                        registration.State == SubjectRegistrationState.Approved &&
                        enrollment.AcademicBatchId == offering.AcademicBatchId &&
                        enrollment.IsCurrent && enrollment.State == EnrollmentState.Active &&
                        student.StatusCode == "Active" && !registration.IsDeleted &&
                        !enrollment.IsDeleted && !student.IsDeleted
                    select registration.Id).ToListAsync(token);
                if (eligibleIds.Count != ids.Length || request.Marks.Any(x =>
                    !x.IsAbsent && x.ObtainedMarks > sub.FullMarks))
                    return Error<bool>("Marks exceed maximum or student is not registered.", 409);
                if (exam.AttemptMode != AssessmentAttemptMode.Regular)
                {
                    if (_attemptRegistrations == null)
                        return Error<bool>("Attempt register is unavailable.", 503);
                    var approved = await ApprovedAttemptQuery(sub.Id)
                        .Where(x => ids.Contains(x.StudentSubjectRegistrationId))
                        .Select(x => x.StudentSubjectRegistrationId).ToListAsync(token);
                    if (approved.Distinct().Count() != ids.Length)
                        return Error<bool>("Retake/improvement marks require approved student-specific attempt authorization.", 409);
                }
                var existing = await _marks.GetQueryable().Where(x => x.TenantId == tenant &&
                    x.AssessmentSubjectId == sub.Id && ids.Contains(x.StudentSubjectRegistrationId) &&
                    !x.IsDeleted).ToDictionaryAsync(x => x.StudentSubjectRegistrationId, token);
                var gradeSchemeId = sub.GradeSchemeId ?? exam.GradeSchemeId;
                var grades = gradeSchemeId.HasValue ? await _grades.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.GradeSchemeId == gradeSchemeId.Value && !x.IsDeleted)
                    .OrderByDescending(x => x.MinMarks).ToListAsync(token) : [];
                var now = _clock.GetUtcNow().UtcDateTime;
                var changedAny = false;
                foreach (var item in request.Marks)
                {
                    existing.TryGetValue(item.StudentSubjectRegistrationId, out var row);
                    if (row != null && !Matches(row.RowVersion, item.RowVersion))
                        return Error<bool>("A student mark was changed; reload and retry.", 409);
                    if (row == null && !string.IsNullOrEmpty(item.RowVersion))
                        return Error<bool>("A student mark no longer exists.", 409);
                    var normalized = item.IsAbsent ? 0m : Round(item.ObtainedMarks);
                    var remarks = Trim(item.Remarks);
                    var percentage = sub.FullMarks <= 0 ? 0m : 100m * normalized / sub.FullMarks;
                    var grade = grades.FirstOrDefault(x => percentage >= x.MinMarks && percentage <= x.MaxMarks);
                    if (!AssessmentMarkChangePolicy.HasChanged(row, normalized, item.IsAbsent,
                        item.IsWithheld, remarks, grade?.GradeLetter, grade?.GradePoint))
                        continue;
                    var created = row == null;
                    var before = row == null ? null : System.Text.Json.JsonSerializer.Serialize(new
                    {
                        registrationId = row.StudentSubjectRegistrationId, obtainedMarks = row.ObtainedMarks,
                        isAbsent = row.IsAbsent, isWithheld = row.IsWithheld,
                        gradeLetter = row.GradeLetter, gradePoint = row.GradePoint
                    });
                    if (row == null)
                    {
                        row = new StudentAssessmentMark
                        {
                            TenantId = tenant, AssessmentSubjectId = sub.Id,
                            StudentSubjectRegistrationId = item.StudentSubjectRegistrationId,
                            CreatedAt = now, CreatedBy = _user.UserId
                        };
                        await _marks.AddAsync(row);
                    }
                    else
                    {
                        row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
                        _marks.Update(row);
                    }
                    row.ObtainedMarks = normalized; row.IsAbsent = item.IsAbsent;
                    row.IsWithheld = item.IsWithheld; row.Remarks = remarks;
                    row.GradeLetter = grade?.GradeLetter; row.GradePoint = grade?.GradePoint;
                    row.EnteredByUserId = _user.UserId; row.EnteredAt = now;
                    await _auditLogs.AddAsync(new AuditLog
                    {
                        TenantId = tenant, UserId = _user.UserId,
                        EntityName = "AssessmentMarkChange", EntityId = sub.Id,
                        Action = created ? "SubjectCreated" : "SubjectUpdated",
                        OldValue = before,
                        NewValue = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            registrationId = item.StudentSubjectRegistrationId, obtainedMarks = normalized,
                            isAbsent = item.IsAbsent, isWithheld = item.IsWithheld,
                            gradeLetter = grade?.GradeLetter, gradePoint = grade?.GradePoint
                        }),
                        IsSuccess = true, OccurredAt = now
                    });
                    changedAny = true;
                }
                if (!changedAny)
                    return ApiResponse<bool>.SuccessResponse(true, "Marks unchanged.");
                // Change the subject evidence version and assessment lock boundary atomically.
                sub.UpdatedAt = now; sub.UpdatedBy = _user.UserId;
                _subjects.Update(sub);
                // A concurrent lock and mark edit must conflict on the assessment rowversion.
                exam.UpdatedAt = now; exam.UpdatedBy = _user.UserId;
                _assessments.Update(exam);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Marks saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Marks changed concurrently.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            _logger.LogWarning(ex, "Concurrent marks transaction deadlock tenant {TenantId}", _user.TenantId);
            return Error<bool>("Marks transaction conflicted; reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Mark entry conflict for tenant {TenantId}", _user.TenantId);
            return Error<bool>("Marks conflict with another update.", 409);
        }
    }

    public Task<ApiResponse<AssessmentResultSheetDto>> PreviewResultsAsync(
        AssessmentScopeDto request, CancellationToken ct = default) => BuildResultAsync(request, false, ct);

    public Task<ApiResponse<AssessmentResultSheetDto>> GetResultsAsync(
        AssessmentScopeDto request, CancellationToken ct = default) => BuildResultAsync(request, true, ct);

    private async Task<ApiResponse<AssessmentResultSheetDto>> BuildResultAsync(
        AssessmentScopeDto request, bool publishedOnly, CancellationToken ct)
    {
        if (!CanPublish()) return Error<AssessmentResultSheetDto>("Result access requires administration.", 403);
        var scope = await ScopeAsync(request, ct);
        if (scope == null) return Error<AssessmentResultSheetDto>("Assessment or batch not found.", 404);
        var tenant = _user.TenantId;
        var publication = await _publications.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AssessmentId == request.AssessmentId &&
            x.AcademicBatchId == request.AcademicBatchId &&
            x.State == ResultPublicationState.Published && !x.IsDeleted)
            .OrderByDescending(x => x.VersionNo).FirstOrDefaultAsync(ct);
        if (publishedOnly)
        {
            if (publication == null) return Error<AssessmentResultSheetDto>("Results have not been published.", 404);
            // Include soft-deleted evidence in the whole-version integrity gate;
            // otherwise a partially removed snapshot could appear valid on readback.
            var publicationEvidence = await GetPublicationEvidenceAsync([publication.Id],
                scope.Value.Exam.CampusId, scope.Value.Exam.AcademicYearId, ct);
            if (!publicationEvidence.TryGetValue(publication.Id, out var snapshotProof) ||
                !snapshotProof.IsReconciled)
                return Error<AssessmentResultSheetDto>(
                    "Published result snapshot requires reconciliation.", 409);
            // Reconcile the entire immutable version before returning any student data.
            var allSummaries = _summaries.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.ResultPublicationId == publication.Id && !x.IsDeleted);
            var snapshotCount = await allSummaries.CountAsync(ct);
            if (snapshotCount == 0 || snapshotCount > 5000 ||
                await allSummaries.GroupBy(x => x.StudentEnrollmentId)
                    .AnyAsync(group => group.Count() > 1, ct))
                return Error<AssessmentResultSheetDto>("Published result snapshot requires reconciliation.", 409);
            var snapshot = await (from row in _summaries.GetQueryable().AsNoTracking()
                join enrollment in _enrollments.GetQueryable().AsNoTracking() on row.StudentEnrollmentId equals enrollment.Id
                join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                where row.TenantId == tenant && enrollment.TenantId == tenant && student.TenantId == tenant &&
                    row.ResultPublicationId == publication.Id &&
                    !row.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                    row.AssessmentId == publication.AssessmentId &&
                    row.PublicationVersionNo == publication.VersionNo &&
                    enrollment.AcademicBatchId == publication.AcademicBatchId &&
                    enrollment.CampusId == scope.Value.Exam.CampusId &&
                    enrollment.AcademicYearId == scope.Value.Exam.AcademicYearId &&
                    row.TotalMarks > 0m && row.ObtainedMarks >= 0m &&
                    row.ObtainedMarks <= row.TotalMarks &&
                    (!row.Percentage.HasValue ||
                        (row.Percentage >= 0m && row.Percentage <= 100m))
                orderby row.MeritPosition, enrollment.RollNo
                select new AssessmentResultItemDto
                {
                    StudentReference = student.PublicId, StudentCode = student.StudentCode,
                    StudentName = student.FullName, RollNo = enrollment.RollNo,
                    TotalMarks = row.ObtainedMarks, MaximumMarks = row.TotalMarks,
                    Percentage = row.Percentage ?? 0m, GradePointAverage = row.GPA,
                    CumulativeGradePointAverage = row.CGPA, CreditHours = row.CreditHours,
                    GradeLetter = row.GradeLetter, MeritPosition = row.MeritPosition,
                    IsPassed = row.IsPassed, IsWithheld = row.IsWithheld
                }).Take(5001).ToListAsync(ct);
            if (snapshot.Count != snapshotCount)
                return Error<AssessmentResultSheetDto>(
                    "Published result snapshot has invalid tenant, campus, version or numeric evidence.", 409);
            var subjects = await _subjects.GetQueryable().AsNoTracking().Join(
                _offerings.GetQueryable().AsNoTracking(), x => x.SubjectOfferingId, y => y.Id,
                (x, y) => new { x, y }).CountAsync(x =>
                x.x.TenantId == tenant && x.y.TenantId == tenant &&
                x.x.AssessmentId == request.AssessmentId &&
                x.y.AcademicBatchId == request.AcademicBatchId, ct);
            return ApiResponse<AssessmentResultSheetDto>.SuccessResponse(new AssessmentResultSheetDto
            {
                AssessmentId = request.AssessmentId, AssessmentName = scope.Value.Exam.Name,
                AcademicYearId = scope.Value.Exam.AcademicYearId,
                AcademicBatchId = request.AcademicBatchId, SubjectCount = subjects,
                PublishedAt = publication.PublishedAt, Results = snapshot
            });
        }
        var calculated = await CalculateAsync(scope.Value.Exam, scope.Value.Batch, ct);
        return calculated.Error == null
            ? ApiResponse<AssessmentResultSheetDto>.SuccessResponse(calculated.Sheet!)
            : Error<AssessmentResultSheetDto>(calculated.Error, 409);
    }

    public async Task<ApiResponse<ResultPublicationDto>> PublishResultAsync(
        PublishResultRequestDto request, CancellationToken ct = default)
    {
        if (!CanPublish()) return Error<ResultPublicationDto>("Publication permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.AssessmentReference == Guid.Empty || request.AcademicBatchId <= 0 ||
            request.PublishNote?.Length > 1000)
            return Error<ResultPublicationDto>("Invalid publication request.");
        var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.PublicId == request.AssessmentReference && !x.IsDeleted, ct);
        if (exam == null) return Error<ResultPublicationDto>("Assessment not found.", 404);
        var scope = await ScopeAsync(new AssessmentScopeDto
        { AssessmentId = exam.Id, AcademicBatchId = request.AcademicBatchId }, ct);
        if (scope == null) return Error<ResultPublicationDto>("Selected batch is outside assessment scope.", 409);
        try
        {
            return await _uow.ExecuteInTransactionAsync(async token =>
            {
                var already = await _publications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == _user.TenantId && x.AssessmentId == exam.Id &&
                    x.AcademicBatchId == request.AcademicBatchId && x.State == ResultPublicationState.Published, token);
                if (already != null)
                {
                    if (already.VisibleToStudent != request.VisibleToStudent ||
                        already.VisibleToGuardian != request.VisibleToGuardian ||
                        already.PublishNote != Trim(request.PublishNote))
                        return Error<ResultPublicationDto>("Existing published result differs from this request.", 409);
                    return ApiResponse<ResultPublicationDto>.SuccessResponse(MapPublication(already, exam.PublicId,
                        scope.Value.Batch.Name), "Result is already published.");
                }
                if (exam.State != AssessmentState.Locked)
                    return Error<ResultPublicationDto>("Assessment must be locked before publishing results.", 409);
                var calculated = await CalculateAsync(exam, scope.Value.Batch, token);
                if (calculated.Error != null) return Error<ResultPublicationDto>(calculated.Error, 409);
                if (calculated.Sheet == null || !CanCaptureCourseSnapshots(calculated.Sheet))
                    return Error<ResultPublicationDto>("Course-grade evidence exceeds the synchronous publication limit or is incomplete.", 409);
                if (_courseSnapshots == null && calculated.Sheet.Results.Any(x => x.CreditHours.HasValue))
                    return Error<ResultPublicationDto>("Credit-based result snapshot storage is not configured.", 503);
                var prior = await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.AssessmentId == exam.Id &&
                    x.AcademicBatchId == request.AcademicBatchId && !x.IsDeleted, token);
                if (prior)
                    return Error<ResultPublicationDto>("Existing publication requires controlled version management.", 409);
                var enrolled = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.AcademicBatchId == scope.Value.Batch.Id &&
                    x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.RollNo, token);
                if (calculated.Sheet!.Results.Any(result => !enrolled.ContainsKey(result.RollNo)))
                    return Error<ResultPublicationDto>("Student enrollment roster changed.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var publication = new ResultPublication
                {
                    TenantId = _user.TenantId, AssessmentId = exam.Id,
                    AcademicBatchId = scope.Value.Batch.Id, VersionNo = 1,
                    State = ResultPublicationState.Published, PublishedAt = now,
                    PublishedByUserId = _user.UserId,
                    VisibleToStudent = request.VisibleToStudent,
                    VisibleToGuardian = request.VisibleToGuardian,
                    HasCourseGradeSnapshots = _courseSnapshots != null,
                    PublishNote = Trim(request.PublishNote),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _publications.AddAsync(publication);
                await _uow.SaveChangesAsync(token);
                foreach (var result in calculated.Sheet.Results)
                {
                    var enrollment = enrolled[result.RollNo];
                    await _summaries.AddAsync(new StudentResultSummary
                    {
                        TenantId = _user.TenantId, ResultPublicationId = publication.Id,
                        AssessmentId = exam.Id, StudentEnrollmentId = enrollment.Id,
                        PublicationVersionNo = 1, TotalMarks = result.MaximumMarks,
                        ObtainedMarks = result.TotalMarks, Percentage = result.Percentage,
                        GPA = result.GradePointAverage, GradeLetter = result.GradeLetter,
                        CGPA = result.CumulativeGradePointAverage,
                        CreditHours = result.CreditHours,
                        CourseCount = _courseSnapshots != null ? result.Courses.Count : null,
                        MeritPosition = result.MeritPosition, IsPassed = result.IsPassed,
                        IsWithheld = result.IsWithheld, CalculatedAt = now,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
                await CaptureCourseSnapshotsAsync(publication, calculated.Sheet,
                    enrolled.ToDictionary(x => x.Key, x => x.Value.Id), now, token);
                await _uow.SaveChangesAsync(token);
                return ApiResponse<ResultPublicationDto>.SuccessResponse(
                    MapPublication(publication, exam.PublicId, scope.Value.Batch.Name), "Results published.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<ResultPublicationDto>("Result publication changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Result publication conflict for tenant {TenantId}", _user.TenantId);
            return Error<ResultPublicationDto>("Duplicate or conflicting publication.", 409);
        }
    }

    private async Task<(AssessmentResultSheetDto? Sheet, string? Error)> CalculateAsync(
        Assessment exam, AcademicBatch batch, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var subjectData = await (from sub in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking() on sub.SubjectOfferingId equals offering.Id
            where sub.TenantId == tenant && offering.TenantId == tenant && sub.AssessmentId == exam.Id &&
                offering.AcademicBatchId == batch.Id && !sub.IsDeleted
            select new { sub, offering }).ToListAsync(ct);
        if (subjectData.Count == 0 || subjectData.Any(x => x.sub.FullMarks <= 0 || x.sub.Weightage <= 0))
            return (null, "Assessment subjects have invalid marks or are missing.");
        var curriculumIds = subjectData.Select(x => x.offering.CurriculumSubjectId).Distinct().ToArray();
        var curriculumItems = await _curriculumSubjects.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant && curriculumIds.Contains(x.Id) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, ct);
        if (subjectData.Any(x => !curriculumItems.ContainsKey(x.offering.CurriculumSubjectId)))
            return (null, "Assessment has a missing curriculum subject.");
        var enrollments = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                enrollment.AcademicBatchId == batch.Id && enrollment.CampusId == batch.CampusId &&
                enrollment.AcademicYearId == batch.AcademicYearId &&
                enrollment.State == EnrollmentState.Active && enrollment.IsCurrent &&
                student.StatusCode == "Active" && !enrollment.IsDeleted && !student.IsDeleted
            select new { enrollment, student }).OrderBy(x => x.enrollment.RollNo).Take(5001).ToListAsync(ct);
        if (enrollments.Count is 0 or > 5000) return (null, "Active roster is empty or too large.");
        HashSet<(long SubjectId, long RegistrationId)>? allowedAttempts = null;
        if (exam.AttemptMode != AssessmentAttemptMode.Regular)
        {
            if (_attemptRegistrations == null)
                return (null, "Attempt register is unavailable.");
            var applicableSubjectIds = subjectData.Select(x => x.sub.Id).ToArray();
            var attemptRows = await _attemptRegistrations.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && applicableSubjectIds.Contains(x.AssessmentSubjectId) &&
                !x.IsDeleted).Select(x => new
                { x.AssessmentSubjectId, x.StudentSubjectRegistrationId, x.PriorResultPublicationId, x.Decision }).ToListAsync(ct);
            if (attemptRows.Count == 0 || attemptRows.Any(x => x.Decision == AssessmentAttemptDecision.Requested))
                return (null, "Targeted retake/improvement has no approved candidates or undecided requests.");
            allowedAttempts = attemptRows.Where(x => x.Decision == AssessmentAttemptDecision.Approved)
                .Select(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId)).ToHashSet();
            if (allowedAttempts.Count == 0)
                return (null, "Targeted retake/improvement has no approved candidates.");
            var sourceIds = attemptRows.Where(x => x.Decision == AssessmentAttemptDecision.Approved)
                .Select(x => x.PriorResultPublicationId).Distinct().ToArray();
            var sourceQuery = _publications.GetQueryable().AsNoTracking();
            var sources = await sourceQuery.Where(x => x.TenantId == tenant && sourceIds.Contains(x.Id) &&
                x.State == ResultPublicationState.Published && x.VisibleToStudent && !x.IsDeleted &&
                !sourceQuery.Any(newer => newer.TenantId == tenant &&
                    newer.AssessmentId == x.AssessmentId && newer.AcademicBatchId == x.AcademicBatchId &&
                    newer.VersionNo > x.VersionNo && !newer.IsDeleted &&
                    (newer.State == ResultPublicationState.Published || newer.State == ResultPublicationState.Withdrawn)))
                .Select(x => x.Id).ToListAsync(ct);
            if (sources.Count != sourceIds.Length)
                return (null, "Approved retake/improvement cites a withdrawn or superseded prior result.");
        }
        var enrollmentIds = enrollments.Select(x => x.enrollment.Id).ToArray();
        var offerings = subjectData.Select(x => x.offering.Id).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollmentIds.Contains(x.StudentEnrollmentId) &&
            offerings.Contains(x.SubjectOfferingId) && x.State == SubjectRegistrationState.Approved &&
            !x.IsDeleted).ToListAsync(ct);
        if (registrations.GroupBy(x => (x.StudentEnrollmentId, x.SubjectOfferingId)).Any(g => g.Count() != 1))
            return (null, "Duplicate approved subject registrations require review.");
        var regByPair = registrations.ToDictionary(x => (x.StudentEnrollmentId, x.SubjectOfferingId));
        var regIds = registrations.Select(x => x.Id).ToArray();
        var subjectIds = subjectData.Select(x => x.sub.Id).ToArray();
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
            regIds.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted).ToListAsync(ct);
        if (marks.GroupBy(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId)).Any(g => g.Count() != 1))
            return (null, "Duplicate subject marks require review.");
        var marksByPair = marks.ToDictionary(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId));
        // One batched read for all configured component definitions and saved details.
        var componentDefinitions = await _components.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
            !x.IsDeleted).ToListAsync(ct);
        var componentsBySubject = componentDefinitions.GroupBy(x => x.AssessmentSubjectId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<AssessmentComponent>)x.ToList());
        var masterIds = marks.Select(x => x.Id).ToArray();
        var componentDetails = await _componentMarks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && masterIds.Contains(x.StudentAssessmentMarkId) &&
            !x.IsDeleted).ToListAsync(ct);
        var detailsByMaster = componentDetails.GroupBy(x => x.StudentAssessmentMarkId)
            .ToDictionary(x => x.Key, x => (IReadOnlyList<StudentAssessmentComponentMark>)x.ToList());
        var scheme = await _gradeSchemes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == exam.GradeSchemeId && x.IsActive && !x.IsDeleted, ct);
        if (scheme == null) return (null, "Assessment requires an active grade scheme.");
        var grades = await _grades.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.GradeSchemeId == scheme.Id && !x.IsDeleted)
            .OrderByDescending(x => x.MinMarks).ToListAsync(ct);
        if (grades.Count == 0) return (null, "Grade scheme has no grading rules.");
        if (exam.CountsTowardCgpa && !scheme.UseCreditWeightedGpa)
            return (null, "Cumulative GPA requires the assessment's credit-weighted grade scheme.");
        var otherSchemeIds = subjectData.Where(x => x.sub.GradeSchemeId.HasValue &&
            x.sub.GradeSchemeId != scheme.Id).Select(x => x.sub.GradeSchemeId!.Value)
            .Distinct().ToArray();
        var subjectGradeRules = await _grades.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && otherSchemeIds.Contains(x.GradeSchemeId) && !x.IsDeleted)
            .OrderByDescending(x => x.MinMarks).ToListAsync(ct);
        var gradeSets = grades.Concat(subjectGradeRules).GroupBy(x => x.GradeSchemeId)
            .ToDictionary(x => x.Key, x => x.ToArray());
        if (scheme.UseCreditWeightedGpa && (exam.GradeSchemeId != scheme.Id ||
            subjectData.Any(x => x.sub.GradeSchemeId.HasValue && x.sub.GradeSchemeId != scheme.Id)))
            return (null, "Credit-weighted GPA requires one consistent assessment grade scheme.");
        var results = new List<AssessmentResultItemDto>();
        foreach (var person in enrollments)
        {
            decimal total = 0, full = 0, weighted = 0, weightSum = 0;
            decimal creditPoints = 0, earnedCredits = 0;
            var withheld = false; var passed = true;
            var courseOutcomes = new List<AssessmentResultCourseDto>();
            foreach (var subject in subjectData)
            {
                var item = curriculumItems[subject.offering.CurriculumSubjectId];
                if (!AssessmentSubjectEligibility.AppliesToEnrollment(item.AcademicCurriculumId,
                    item.AcademicLevelId, person.enrollment.AcademicCurriculumId,
                    person.enrollment.AcademicLevelId))
                    continue;
                var registered = regByPair.TryGetValue(
                    (person.enrollment.Id, subject.offering.Id), out var registration);
                if (allowedAttempts != null && (!registered ||
                    !allowedAttempts.Contains((subject.sub.Id, registration!.Id))))
                    continue;
                if (!AssessmentSubjectEligibility.RequiresMark(item.IsOptional, registered))
                    continue;
                if (!registered || !marksByPair.TryGetValue((subject.sub.Id, registration!.Id), out var mark))
                    return (null, "Mandatory registration or approved subject mark is missing.");
                if (mark.ObtainedMarks < 0 || mark.ObtainedMarks > subject.sub.FullMarks ||
                    mark.IsAbsent && mark.ObtainedMarks != 0)
                    return (null, "Invalid subject mark requires review.");
                componentsBySubject.TryGetValue(subject.sub.Id, out var definitions);
                detailsByMaster.TryGetValue(mark.Id, out var details);
                var componentCheck = AssessmentComponentMarkPolicy.Evaluate(
                    subject.sub.FullMarks, mark.ObtainedMarks, mark.IsAbsent,
                    mark.IsWithheld, definitions ?? Array.Empty<AssessmentComponent>(),
                    details ?? Array.Empty<StudentAssessmentComponentMark>());
                if (!componentCheck.IsReconciled)
                    return (null, "Component marks require reconciliation before publication.");
                var selectedGradeScheme = subject.sub.GradeSchemeId ?? scheme.Id;
                if (!gradeSets.TryGetValue(selectedGradeScheme, out var subjectGrades))
                    return (null, "Course grading policy is missing.");
                var subjectPercent = Round(100m * mark.ObtainedMarks / subject.sub.FullMarks);
                var subjectGrade = subjectGrades.FirstOrDefault(x =>
                    subjectPercent >= x.MinMarks && subjectPercent <= x.MaxMarks);
                if (subjectGrade == null)
                    return (null, "No grade rule matches an individual course percentage.");
                var credits = registration!.CreditHoursSnapshot ?? item.CreditHours;
                if (credits < 0 || credits > 100 || decimal.Round(credits, 2) != credits)
                    return (null, "Configured course credit hours require reconciliation.");
                passed &= componentCheck.MandatoryComponentsPassed;
                full += subject.sub.FullMarks; total += mark.ObtainedMarks;
                weighted += mark.ObtainedMarks / subject.sub.FullMarks * subject.sub.Weightage;
                weightSum += subject.sub.Weightage;
                withheld |= mark.IsWithheld;
                passed &= !mark.IsAbsent && !mark.IsWithheld &&
                    mark.ObtainedMarks >= subject.sub.PassMarks && !subjectGrade.IsFailGrade;
                courseOutcomes.Add(new AssessmentResultCourseDto
                {
                    AssessmentSubjectId = subject.sub.Id,
                    CurriculumSubjectId = subject.offering.CurriculumSubjectId,
                    StudentSubjectRegistrationId = registration.Id,
                    FullMarks = subject.sub.FullMarks, ObtainedMarks = mark.ObtainedMarks,
                    CreditHours = credits, GradeLetter = subjectGrade.GradeLetter,
                    GradePoint = subjectGrade.GradePoint, IsAbsent = mark.IsAbsent,
                    IsWithheld = mark.IsWithheld,
                    IsPassed = componentCheck.MandatoryComponentsPassed && !mark.IsAbsent &&
                        !mark.IsWithheld && mark.ObtainedMarks >= subject.sub.PassMarks &&
                        !subjectGrade.IsFailGrade
                });
                if (scheme.UseCreditWeightedGpa)
                {
                    if (credits <= 0)
                        return (null, "Credit-weighted GPA requires valid positive credit hours for each included course.");
                    earnedCredits += credits;
                    creditPoints += credits * subjectGrade.GradePoint;
                }
            }
            if (weightSum <= 0)
            {
                if (allowedAttempts != null) continue;
                return (null, "Student has no applicable registered assessment subjects.");
            }
            var percent = decimal.Round(100m * weighted / weightSum, 4);
            var grade = grades.FirstOrDefault(x => percent >= x.MinMarks && percent <= x.MaxMarks);
            if (grade == null) return (null, "No grade rule matches the result percentage.");
            if (scheme.UseCreditWeightedGpa && earnedCredits <= 0)
                return (null, "Credit-weighted GPA requires registered courses with positive credits.");
            results.Add(new AssessmentResultItemDto
            {
                StudentReference = person.student.PublicId,
                StudentCode = person.student.StudentCode, StudentName = person.student.FullName,
                RollNo = person.enrollment.RollNo, TotalMarks = Round(total),
                MaximumMarks = Round(full), Percentage = percent,
                GradePointAverage = scheme.UseCreditWeightedGpa
                    ? decimal.Round(creditPoints / earnedCredits, 4, MidpointRounding.AwayFromZero)
                    : grade.GradePoint,
                CreditHours = scheme.UseCreditWeightedGpa ? earnedCredits : null,
                Courses = courseOutcomes,
                GradeLetter = grade.GradeLetter,
                IsPassed = passed && !grade.IsFailGrade, IsWithheld = withheld
            });
        }
        var resultRolls = results.Select(x => x.RollNo).ToHashSet();
        var cumulativeError = await ApplyCumulativeGradePointsAsync(exam, batch,
            enrollments.Where(x => resultRolls.Contains(x.enrollment.RollNo))
                .Select(x => (Enrollment: x.enrollment, Roll: x.enrollment.RollNo)).ToArray(),
            results, ct);
        if (cumulativeError != null) return (null, cumulativeError);
        var rank = 0;
        foreach (var row in results.OrderBy(x => x.IsWithheld).ThenByDescending(x => x.IsPassed)
            .ThenByDescending(x => x.TotalMarks).ThenBy(x => x.RollNo))
            if (!row.IsWithheld) row.MeritPosition = ++rank;
        return (new AssessmentResultSheetDto
        {
            AssessmentId = exam.Id, AssessmentName = exam.Name,
            AcademicYearId = exam.AcademicYearId, AcademicBatchId = batch.Id,
            SubjectCount = subjectData.Count, Results = results
        }, null);
    }

    private async Task<(Assessment Exam, AcademicBatch Batch)?> ScopeAsync(
        AssessmentScopeDto? request, CancellationToken ct)
    {
        if (request == null || request.AssessmentId <= 0 || request.AcademicBatchId <= 0) return null;
        var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == request.AssessmentId && !x.IsDeleted, ct);
        if (exam == null || !await CanAccessCampusAsync(exam.CampusId, ct)) return null;
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == request.AcademicBatchId &&
            x.CampusId == exam.CampusId && x.AcademicYearId == exam.AcademicYearId &&
            x.IsActive && !x.IsDeleted, ct);
        return batch == null ? null : (exam, batch);
    }

    private async Task<List<EligibleRow>> EligibleAsync(long offeringId, long batchId, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        return await (from registration in _registrations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on registration.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where registration.TenantId == tenant && enrollment.TenantId == tenant && student.TenantId == tenant &&
                registration.SubjectOfferingId == offeringId && registration.State == SubjectRegistrationState.Approved &&
                enrollment.AcademicBatchId == batchId && enrollment.IsCurrent &&
                enrollment.State == EnrollmentState.Active && student.StatusCode == "Active" &&
                !registration.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
            orderby enrollment.RollNo, student.Id
            select new EligibleRow
            {
                RegistrationId = registration.Id, StudentReference = student.PublicId,
                StudentCode = student.StudentCode, StudentName = student.FullName, Roll = enrollment.RollNo
            }).Take(2001).ToListAsync(ct);
    }

    private async Task<bool> CanEditOfferingAsync(long offeringId, CancellationToken ct)
    {
        if (CanPublish()) return await CanAccessOfferingAsync(offeringId, ct);
        var employeeId = await OwnTeacherIdAsync(ct);
        return employeeId > 0 && await _instructors.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == _user.TenantId && x.SubjectOfferingId == offeringId &&
            x.EmployeeId == employeeId && x.IsActive && !x.IsDeleted, ct);
    }

    private async Task<long> OwnTeacherIdAsync(CancellationToken ct) =>
        await _employees.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.UserId == _user.UserId &&
            x.CanTeach && x.State == EmployeeState.Active && !x.IsDeleted)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);

    private sealed class EligibleRow
    {
        public long RegistrationId { get; set; }
        public Guid StudentReference { get; set; }
        public string StudentCode { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string Roll { get; set; } = "";
    }

    private static ResultPublicationDto MapPublication(ResultPublication row,
        Guid assessmentReference, string batchName) => new()
    {
        Id = row.Id, AssessmentReference = assessmentReference,
        AcademicBatchId = row.AcademicBatchId, AcademicBatchName = batchName,
        State = row.State, PublishedAt = row.PublishedAt,
        PublishedByUserId = row.PublishedByUserId, VisibleToStudent = row.VisibleToStudent,
        VisibleToGuardian = row.VisibleToGuardian, PublishNote = row.PublishNote,
        RowVersion = Version(row.RowVersion)
    };

    private async Task<ApiResponse<T>> WriteAsync<T>(
        string name, Func<CancellationToken, Task<ApiResponse<T>>> action, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(action, ct); }
        catch (DbUpdateConcurrencyException) { return Error<T>("Assessment changed concurrently.", 409); }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number == 1205)
        {
            _logger.LogWarning(ex, "Assessment transaction deadlock on {Action} tenant {TenantId}", name, _user.TenantId);
            return Error<T>("Assessment transaction conflicted; reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Assessment write conflict on {Action} tenant {TenantId}", name, _user.TenantId);
            return Error<T>("Assessment conflicts with an existing update.", 409);
        }
    }


    // Tenant-wide assessment roles retain their existing policy. Principal/VicePrincipal
    // must have an explicit active grant for every campus-scoped read or mutation.
    private bool IsRestrictedCampusManager() => !_user.IsTenantAdmin &&
        (_user.IsInRole("Principal") || _user.IsInRole("VicePrincipal"));

    private IQueryable<long> AllowedCampusIds(long tenant) =>
        from access in _campusAccesses.GetQueryable().AsNoTracking()
        join campus in _campuses.GetQueryable().AsNoTracking() on access.CampusId equals campus.Id
        where access.TenantId == tenant && campus.TenantId == tenant &&
            access.UserId == _user.UserId && access.IsActive && !access.IsDeleted &&
            campus.IsActive && !campus.IsDeleted
        select campus.Id;

    private Task<bool> CanAccessCampusAsync(long campusId, CancellationToken ct) =>
        !IsRestrictedCampusManager() ? Task.FromResult(true) :
        AllowedCampusIds(_user.TenantId).AnyAsync(id => id == campusId, ct);

    private Task<bool> CanAccessOfferingAsync(long offeringId, CancellationToken ct)
    {
        if (!IsRestrictedCampusManager()) return Task.FromResult(true);
        var tenant = _user.TenantId;
        return (from offering in _offerings.GetQueryable().AsNoTracking()
                join batch in _batches.GetQueryable().AsNoTracking()
                    on offering.AcademicBatchId equals batch.Id
                where offering.TenantId == tenant && batch.TenantId == tenant &&
                    offering.Id == offeringId && !offering.IsDeleted && !batch.IsDeleted &&
                    AllowedCampusIds(tenant).Contains(batch.CampusId)
                select offering.Id).AnyAsync(ct);
    }

    private Task<bool> CanAccessEnrollmentAsync(long enrollmentId, CancellationToken ct)
    {
        if (!IsRestrictedCampusManager()) return Task.FromResult(true);
        var tenant = _user.TenantId;
        return _enrollments.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenant && x.Id == enrollmentId && !x.IsDeleted &&
            AllowedCampusIds(tenant).Contains(x.CampusId), ct);
    }

    // A transcript covers a student's complete academic history. Do not expose
    // mixed-campus history to a manager with access to only one of those campuses.
    private async Task<bool> CanAccessStudentHistoryAsync(long studentId, CancellationToken ct)
    {
        if (!IsRestrictedCampusManager()) return true;
        var tenant = _user.TenantId;
        var rows = _enrollments.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.StudentId == studentId && !x.IsDeleted);
        return await rows.AnyAsync(x => AllowedCampusIds(tenant).Contains(x.CampusId), ct) &&
            !await rows.AnyAsync(x => !AllowedCampusIds(tenant).Contains(x.CampusId), ct);
    }

    private bool CanRead() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (CanPublish() || _user.IsInRole("Teacher") || _user.IsInRole("ExamController"));
    private bool CanPublish() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal") || _user.IsInRole("VicePrincipal") ||
         _user.IsInRole("ExamController"));
    private static decimal Round(decimal n) => decimal.Round(n, 2, MidpointRounding.AwayFromZero);
    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    private static string Version(byte[] bytes) => Convert.ToBase64String(bytes);
    private static bool TryVersion(string? s, out byte[] value)
    {
        value = [];
        if (string.IsNullOrWhiteSpace(s)) return false;
        try { value = Convert.FromBase64String(s); return value.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool Matches(byte[] bytes, string? version) =>
        TryVersion(version, out var expected) && Matches(bytes, expected);
    private static bool Matches(byte[] bytes, byte[] expected) =>
        bytes != null && bytes.Length == expected.Length && bytes.Length > 0 &&
        CryptographicOperations.FixedTimeEquals(bytes, expected);
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
}
