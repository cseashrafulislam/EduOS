using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Academic;
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
using System.Security.Cryptography;

namespace EduOS.Service.Services.Exams;

public sealed partial class ExamWorkflowService : IAssessmentAdministrationService
{
    private readonly IGenericRepository<Assessment> _assessments;
    private readonly IGenericRepository<AssessmentSubject> _subjects;
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
    private readonly IGenericRepository<Room> _rooms;
    private readonly IGenericRepository<Student> _students;
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
    private readonly ILogger<ExamWorkflowService> _logger;

    public ExamWorkflowService(IGenericRepository<Assessment> assessments,
        IGenericRepository<AssessmentSubject> subjects, IGenericRepository<AssessmentSchedule> schedules,
        IGenericRepository<StudentAssessmentMark> marks, IGenericRepository<ResultPublication> publications,
        IGenericRepository<StudentResultSummary> summaries, IGenericRepository<GradeScheme> gradeSchemes,
        IGenericRepository<GradeRule> grades, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Subject> subjectNames, IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<SubjectOffering> offerings, IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms, IGenericRepository<Campus> campuses,
        IGenericRepository<Room> rooms, IGenericRepository<Student> students,
        IGenericRepository<Employee> employees, IGenericRepository<InstructorAssignment> instructors,
        IGenericRepository<AssessmentComponent> components,
        IGenericRepository<StudentAssessmentComponentMark> componentMarks,
        IGenericRepository<CertificateTemplate> templates, IGenericRepository<CertificateIssue> certificates,
        IGenericRepository<TranscriptIssue> transcripts, IGenericRepository<AuditLog> auditLogs,
        IUnitOfWork uow, ICurrentUserService user,
        TimeProvider clock, ILogger<ExamWorkflowService> logger)
    {
        _assessments = assessments; _subjects = subjects; _schedules = schedules;
        _marks = marks; _publications = publications; _summaries = summaries;
        _gradeSchemes = gradeSchemes; _grades = grades; _enrollments = enrollments;
        _subjectNames = subjectNames; _registrations = registrations; _offerings = offerings;
        _curriculumSubjects = curriculumSubjects; _batches = batches; _years = years;
        _terms = terms; _campuses = campuses; _rooms = rooms; _students = students;
        _employees = employees; _instructors = instructors; _components = components;
        _componentMarks = componentMarks; _templates = templates;
        _certificates = certificates; _transcripts = transcripts; _auditLogs = auditLogs;
        _uow = uow; _user = user; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>> GetAvailableScopesAsync(
        CancellationToken ct = default)
    {
        if (!CanRead()) return Error<IReadOnlyList<AssessmentScopeOptionDto>>("Assessment permission required.", 403);
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
                !exam.IsDeleted && !sub.IsDeleted
            select new { exam, sub, offering, batch, subject };
        if (!CanPublish())
        {
            var employeeId = await OwnTeacherIdAsync(ct);
            if (employeeId == 0)
                return ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>.SuccessResponse([]);
            var allowed = _instructors.GetQueryable().AsNoTracking().Where(x =>
                x.TenantId == tenant && x.EmployeeId == employeeId && x.IsActive)
                .Select(x => x.SubjectOfferingId);
            q = q.Where(x => allowed.Contains(x.offering.Id));
        }
        IReadOnlyList<AssessmentScopeOptionDto> rows = await q.OrderByDescending(x => x.exam.StartDate)
            .ThenBy(x => x.exam.Id).ThenBy(x => x.batch.Id).ThenBy(x => x.sub.Id)
            .Take(500).Select(x => new AssessmentScopeOptionDto
            {
                AssessmentId = x.exam.Id, AssessmentName = x.exam.Name,
                AcademicYearId = x.exam.AcademicYearId, AcademicBatchId = x.batch.Id,
                AcademicBatchName = x.batch.Name, AssessmentSubjectId = x.sub.Id,
                SubjectName = x.subject.Name, State = x.exam.State
            }).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<AssessmentScopeOptionDto>>.SuccessResponse(rows);
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
                var sub = await _subjects.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AssessmentSubjectId && !x.IsDeleted, token);
                if (sub == null) return Error<bool>("Assessment subject not found.", 404);
                var exam = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == sub.AssessmentId && !x.IsDeleted, token);
                if (exam == null || exam.State is not (AssessmentState.MarksEntry or AssessmentState.InProgress))
                    return Error<bool>("Assessment is not open for marks entry.", 409);
                if (!await CanEditOfferingAsync(sub.SubjectOfferingId, token))
                    return Error<bool>("Instructor assignment not found.", 403);
                var offering = await _offerings.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == sub.SubjectOfferingId && x.IsActive, token);
                if (offering == null) return Error<bool>("Subject offering inactive.", 409);
                if (await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AssessmentId == exam.Id &&
                    x.AcademicBatchId == offering.AcademicBatchId &&
                    x.State == ResultPublicationState.Published, token))
                    return Error<bool>("Published marks cannot be edited.", 409);
                var eligibility = await EligibleAsync(offering.Id, offering.AcademicBatchId, token);
                var eligibleIds = eligibility.Select(x => x.RegistrationId).ToHashSet();
                if (request.Marks.Any(x => !eligibleIds.Contains(x.StudentSubjectRegistrationId) ||
                    !x.IsAbsent && x.ObtainedMarks > sub.FullMarks))
                    return Error<bool>("Marks exceed maximum or student is not registered.", 409);
                var ids = request.Marks.Select(x => x.StudentSubjectRegistrationId).ToArray();
                var existing = await _marks.GetQueryable().Where(x => x.TenantId == tenant &&
                    x.AssessmentSubjectId == sub.Id && ids.Contains(x.StudentSubjectRegistrationId) &&
                    !x.IsDeleted).ToDictionaryAsync(x => x.StudentSubjectRegistrationId, token);
                var gradeSchemeId = sub.GradeSchemeId ?? exam.GradeSchemeId;
                var grades = gradeSchemeId.HasValue ? await _grades.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && x.GradeSchemeId == gradeSchemeId.Value && !x.IsDeleted)
                    .OrderByDescending(x => x.MinMarks).ToListAsync(token) : [];
                var now = _clock.GetUtcNow().UtcDateTime;
                foreach (var item in request.Marks)
                {
                    existing.TryGetValue(item.StudentSubjectRegistrationId, out var row);
                    if (row != null && !Matches(row.RowVersion, item.RowVersion))
                        return Error<bool>("A student mark was changed; reload and retry.", 409);
                    if (row == null && !string.IsNullOrEmpty(item.RowVersion))
                        return Error<bool>("A student mark no longer exists.", 409);
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
                    row.ObtainedMarks = item.IsAbsent ? 0m : Round(item.ObtainedMarks);
                    row.IsAbsent = item.IsAbsent; row.IsWithheld = item.IsWithheld;
                    row.Remarks = Trim(item.Remarks);
                    var percentage = sub.FullMarks <= 0 ? 0m :
                        100m * row.ObtainedMarks / sub.FullMarks;
                    var grade = grades.FirstOrDefault(x => percentage >= x.MinMarks && percentage <= x.MaxMarks);
                    row.GradeLetter = grade?.GradeLetter; row.GradePoint = grade?.GradePoint;
                    row.EnteredByUserId = _user.UserId; row.EnteredAt = now;
                }
                await _uow.SaveChangesAsync(token);
                return ApiResponse<bool>.SuccessResponse(true, "Marks saved.");
            }, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<bool>("Marks changed concurrently.", 409); }
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
            var snapshot = await (from row in _summaries.GetQueryable().AsNoTracking()
                join enrollment in _enrollments.GetQueryable().AsNoTracking() on row.StudentEnrollmentId equals enrollment.Id
                join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
                where row.TenantId == tenant && enrollment.TenantId == tenant && student.TenantId == tenant &&
                    row.ResultPublicationId == publication.Id
                orderby row.MeritPosition, enrollment.RollNo
                select new AssessmentResultItemDto
                {
                    StudentReference = student.PublicId, StudentCode = student.StudentCode,
                    StudentName = student.FullName, RollNo = enrollment.RollNo,
                    TotalMarks = row.ObtainedMarks, MaximumMarks = row.TotalMarks,
                    Percentage = row.Percentage ?? 0m, GradePointAverage = row.GPA,
                    GradeLetter = row.GradeLetter, MeritPosition = row.MeritPosition,
                    IsPassed = row.IsPassed, IsWithheld = row.IsWithheld
                }).Take(5001).ToListAsync(ct);
            if (snapshot.Count > 5000)
                return Error<AssessmentResultSheetDto>("Published result exceeds synchronous report limit.", 409);
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
                var prior = await _publications.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == _user.TenantId && x.AssessmentId == exam.Id &&
                    x.AcademicBatchId == request.AcademicBatchId && !x.IsDeleted, token);
                if (prior)
                    return Error<ResultPublicationDto>("Existing publication requires controlled version management.", 409);
                var now = _clock.GetUtcNow().UtcDateTime;
                var publication = new ResultPublication
                {
                    TenantId = _user.TenantId, AssessmentId = exam.Id,
                    AcademicBatchId = scope.Value.Batch.Id, VersionNo = 1,
                    State = ResultPublicationState.Published, PublishedAt = now,
                    PublishedByUserId = _user.UserId,
                    VisibleToStudent = request.VisibleToStudent,
                    VisibleToGuardian = request.VisibleToGuardian,
                    PublishNote = Trim(request.PublishNote),
                    CreatedAt = now, CreatedBy = _user.UserId
                };
                await _publications.AddAsync(publication);
                await _uow.SaveChangesAsync(token);
                var enrolled = await _enrollments.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == _user.TenantId && x.AcademicBatchId == scope.Value.Batch.Id &&
                    x.State == EnrollmentState.Active && x.IsCurrent && !x.IsDeleted)
                    .ToDictionaryAsync(x => x.RollNo, token);
                foreach (var result in calculated.Sheet!.Results)
                {
                    if (!enrolled.TryGetValue(result.RollNo, out var enrollment))
                        return Error<ResultPublicationDto>("Student enrollment roster changed.", 409);
                    await _summaries.AddAsync(new StudentResultSummary
                    {
                        TenantId = _user.TenantId, ResultPublicationId = publication.Id,
                        AssessmentId = exam.Id, StudentEnrollmentId = enrollment.Id,
                        PublicationVersionNo = 1, TotalMarks = result.MaximumMarks,
                        ObtainedMarks = result.TotalMarks, Percentage = result.Percentage,
                        GPA = result.GradePointAverage, GradeLetter = result.GradeLetter,
                        MeritPosition = result.MeritPosition, IsPassed = result.IsPassed,
                        IsWithheld = result.IsWithheld, CalculatedAt = now,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                }
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
        var enrollments = await (from enrollment in _enrollments.GetQueryable().AsNoTracking()
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where enrollment.TenantId == tenant && student.TenantId == tenant &&
                enrollment.AcademicBatchId == batch.Id && enrollment.State == EnrollmentState.Active &&
                enrollment.IsCurrent && student.StatusCode == "Active" && !enrollment.IsDeleted
            select new { enrollment, student }).OrderBy(x => x.enrollment.RollNo).Take(5001).ToListAsync(ct);
        if (enrollments.Count is 0 or > 5000) return (null, "Active roster is empty or too large.");
        var enrollmentIds = enrollments.Select(x => x.enrollment.Id).ToArray();
        var offerings = subjectData.Select(x => x.offering.Id).ToArray();
        var registrations = await _registrations.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && enrollmentIds.Contains(x.StudentEnrollmentId) &&
            offerings.Contains(x.SubjectOfferingId) && x.State == SubjectRegistrationState.Approved &&
            !x.IsDeleted).ToListAsync(ct);
        var regByPair = registrations.ToDictionary(x => (x.StudentEnrollmentId, x.SubjectOfferingId));
        var regIds = registrations.Select(x => x.Id).ToArray();
        var subjectIds = subjectData.Select(x => x.sub.Id).ToArray();
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && subjectIds.Contains(x.AssessmentSubjectId) &&
            regIds.Contains(x.StudentSubjectRegistrationId) && !x.IsDeleted).ToListAsync(ct);
        var marksByPair = marks.ToDictionary(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId));
        var scheme = await _gradeSchemes.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == exam.GradeSchemeId && x.IsActive && !x.IsDeleted, ct);
        if (scheme == null) return (null, "Assessment requires an active grade scheme.");
        var grades = await _grades.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.GradeSchemeId == scheme.Id && !x.IsDeleted)
            .OrderByDescending(x => x.MinMarks).ToListAsync(ct);
        if (grades.Count == 0) return (null, "Grade scheme has no grading rules.");
        var results = new List<AssessmentResultItemDto>();
        foreach (var person in enrollments)
        {
            decimal total = 0, full = 0, weighted = 0, weightSum = 0;
            var withheld = false; var passed = true;
            foreach (var subject in subjectData)
            {
                if (!regByPair.TryGetValue((person.enrollment.Id, subject.offering.Id), out var registration) ||
                    !marksByPair.TryGetValue((subject.sub.Id, registration.Id), out var mark))
                    return (null, "Assessment has unregistered students or missing marks.");
                full += subject.sub.FullMarks; total += mark.ObtainedMarks;
                weighted += mark.ObtainedMarks / subject.sub.FullMarks * subject.sub.Weightage;
                weightSum += subject.sub.Weightage;
                withheld |= mark.IsWithheld;
                passed &= !mark.IsAbsent && !mark.IsWithheld &&
                    mark.ObtainedMarks >= subject.sub.PassMarks;
            }
            var percent = weightSum > 0 ? decimal.Round(100m * weighted / weightSum, 4) : 0m;
            var grade = grades.FirstOrDefault(x => percent >= x.MinMarks && percent <= x.MaxMarks);
            if (grade == null) return (null, "No grade rule matches the result percentage.");
            results.Add(new AssessmentResultItemDto
            {
                StudentReference = person.student.PublicId,
                StudentCode = person.student.StudentCode, StudentName = person.student.FullName,
                RollNo = person.enrollment.RollNo, TotalMarks = Round(total),
                MaximumMarks = Round(full), Percentage = percent,
                GradePointAverage = grade.GradePoint, GradeLetter = grade.GradeLetter,
                IsPassed = passed && !grade.IsFailGrade, IsWithheld = withheld
            });
        }
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
        if (exam == null) return null;
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
        if (CanPublish()) return true;
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
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Assessment write conflict on {Action} tenant {TenantId}", name, _user.TenantId);
            return Error<T>("Assessment conflicts with an existing update.", 409);
        }
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
