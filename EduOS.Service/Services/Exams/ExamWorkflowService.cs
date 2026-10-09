using EduOS.Core.Common;
using EduOS.Core.DTOs.Exams;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Transactions;

namespace EduOS.Service.Services.Exams;

public sealed class ExamWorkflowService : IExamWorkflowService
{
    private readonly IGenericRepository<Assessment> _assessments;
    private readonly IGenericRepository<AssessmentSubject> _subjects;
    private readonly IGenericRepository<AssessmentSchedule> _schedules;
    private readonly IGenericRepository<StudentAssessmentMark> _marks;
    private readonly IGenericRepository<ResultPublication> _publications;
    private readonly IGenericRepository<StudentResultSummary> _summaries;
    private readonly IGenericRepository<GradeRule> _grades;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<EduOS.Core.Entities.Academic.Subject> _subjectNames;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Employee> _employees;
    private readonly IGenericRepository<InstructorAssignment> _instructors;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExamWorkflowService> _logger;

    public ExamWorkflowService(IGenericRepository<Assessment> assessments,
        IGenericRepository<AssessmentSubject> subjects, IGenericRepository<AssessmentSchedule> schedules,
        IGenericRepository<StudentAssessmentMark> marks,
        IGenericRepository<ResultPublication> publications,
        IGenericRepository<StudentResultSummary> summaries,
        IGenericRepository<GradeRule> grades,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<EduOS.Core.Entities.Academic.Subject> subjectNames,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<Student> students, IGenericRepository<Employee> employees,
        IGenericRepository<InstructorAssignment> instructors,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser,
        TimeProvider clock, ILogger<ExamWorkflowService> logger)
    {
        _assessments = assessments; _subjects = subjects; _schedules = schedules;
        _marks = marks; _publications = publications; _summaries = summaries; _grades = grades;
        _enrollments = enrollments; _subjectNames = subjectNames; _registrations = registrations; _offerings = offerings;
        _curriculumSubjects = curriculumSubjects; _batches = batches; _students = students;
        _employees = employees; _instructors = instructors;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<ExamWorkflowScopeOptionDto>>> GetScopeOptionsAsync(CancellationToken ct = default)
    {
        if (!CanAccess()) return Fail<IReadOnlyList<ExamWorkflowScopeOptionDto>>("Assessment access is required.", 403);
        var tenant = _user.TenantId;
        IQueryable<long>? assignedOfferings = null;
        if (!CanPublish())
        {
            var employeeId = await _employees.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.UserId == _user.UserId &&
                            x.CanTeach && x.State == EmployeeState.Active)
                .Select(x => x.Id).FirstOrDefaultAsync(ct);
            if (employeeId <= 0)
                return ApiResponse<IReadOnlyList<ExamWorkflowScopeOptionDto>>.SuccessResponse(Array.Empty<ExamWorkflowScopeOptionDto>());
            assignedOfferings = _instructors.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.EmployeeId == employeeId && x.IsActive)
                .Select(x => x.SubjectOfferingId);
        }

        var query =
            from exam in _assessments.GetQueryable().AsNoTracking()
            join examSubject in _subjects.GetQueryable().AsNoTracking()
                on exam.Id equals examSubject.AssessmentId
            join offering in _offerings.GetQueryable().AsNoTracking()
                on examSubject.SubjectOfferingId equals offering.Id
            join batch in _batches.GetQueryable().AsNoTracking()
                on offering.AcademicBatchId equals batch.Id
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            join subject in _subjectNames.GetQueryable().AsNoTracking()
                on curriculum.SubjectId equals subject.Id
            where exam.TenantId == tenant && examSubject.TenantId == tenant &&
                  offering.TenantId == tenant && batch.TenantId == tenant &&
                  curriculum.TenantId == tenant && subject.TenantId == tenant &&
                  exam.State != AssessmentState.Cancelled && batch.IsActive && offering.IsActive &&
                  exam.AcademicYearId == batch.AcademicYearId &&
                  exam.CampusId == batch.CampusId &&
                  offering.AcademicYearId == exam.AcademicYearId
            select new { exam, examSubject, offering, batch, curriculum, subject };

        if (assignedOfferings != null)
            query = query.Where(x => assignedOfferings.Contains(x.offering.Id));
        IReadOnlyList<ExamWorkflowScopeOptionDto> rows = await query
            .OrderByDescending(x => x.exam.StartDate)
            .ThenBy(x => x.exam.Id).ThenBy(x => x.batch.Name).ThenBy(x => x.subject.Name)
            .Select(x => new ExamWorkflowScopeOptionDto
            {
                ExamId = x.exam.Id,
                ExamName = x.exam.Name,
                AcademicYearId = x.exam.AcademicYearId,
                ClassId = x.batch.AcademicLevelId,
                SectionId = x.batch.Id,
                SectionName = x.batch.Name,
                SubjectId = x.curriculum.SubjectId,
                SubjectName = x.subject.Name,
                AssessmentState = (int)x.exam.State
            })
            .Distinct().Take(500).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<ExamWorkflowScopeOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<ExamMarkRosterDto>> GetMarkRosterAsync(ExamMarkRosterQueryDto request,
        CancellationToken ct = default)
    {
        if (!CanAccess()) return Fail<ExamMarkRosterDto>("Assessment access is required.", 403);
        var loaded = await LoadScopeAsync(request, ct);
        if (loaded.Error != null) return Fail<ExamMarkRosterDto>(loaded.Error, loaded.Code);
        var subject = await LoadSubjectAsync(request, ct);
        if (subject == null) return Fail<ExamMarkRosterDto>("Subject is not scheduled for the selected assessment and batch.", 409);
        if (!await CanEnterMarksAsync(subject.Value.Offering.Id, ct))
            return Fail<ExamMarkRosterDto>("Not authorized for the selected subject offering.", 403);
        return ApiResponse<ExamMarkRosterDto>.SuccessResponse(
            await RosterAsync(request, loaded.Assessment!, subject.Value.Subject, subject.Value.Offering, ct));
    }

    public async Task<ApiResponse<ExamMarkRosterDto>> SaveMarksAsync(SaveExamMarksDto request,
        CancellationToken ct = default)
    {
        if (!CanAccess()) return Fail<ExamMarkRosterDto>("Assessment access is required.", 403);
        if (request?.Items == null || request.Items.Count == 0 || request.Items.Count > 250 ||
            request.Items.Any(x => x.StudentId <= 0 && x.StudentReference == Guid.Empty))
            return Fail<ExamMarkRosterDto>("Valid marks and student references are required.");
        var loaded = await LoadScopeAsync(request, ct);
        if (loaded.Error != null) return Fail<ExamMarkRosterDto>(loaded.Error, loaded.Code);
        var subject = await LoadSubjectAsync(request, ct);
        if (subject == null) return Fail<ExamMarkRosterDto>("Assessment subject is unavailable.", 409);
        if (!await CanEnterMarksAsync(subject.Value.Offering.Id, ct))
            return Fail<ExamMarkRosterDto>("Not authorized to submit marks for this subject.", 403);
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var assessment = await _assessments.GetQueryable().FirstAsync(x =>
                x.Id == request.ExamId && x.TenantId == tenant, ct);
            if (assessment.State is AssessmentState.Published or AssessmentState.Cancelled or AssessmentState.Locked)
                return Fail<ExamMarkRosterDto>("Marks are locked for this assessment.", 409);
            if (await _publications.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.AssessmentId == assessment.Id && x.AcademicBatchId == request.SectionId &&
                x.State == ResultPublicationState.Published, ct))
                return Fail<ExamMarkRosterDto>("Published results are immutable.", 409);
            var eligible = await EligibleAsync(subject.Value.Offering.Id, request.SectionId, ct);
            var byId = eligible.ToDictionary(x => x.StudentId);
            var byReference = eligible.ToDictionary(x => x.StudentReference);
            var normalized = new List<(long RegistrationId, long StudentId, decimal Marks, bool Absent)>();
            foreach (var item in request.Items)
            {
                EligibleRow? student = null;
                if (item.StudentReference != Guid.Empty) byReference.TryGetValue(item.StudentReference, out student);
                else byId.TryGetValue(item.StudentId, out student);
                if (student == null || item.StudentId > 0 && item.StudentId != student.StudentId ||
                    item.ObtainedMark < 0m || !item.IsAbsent && item.ObtainedMark > subject.Value.Subject.FullMarks)
                    return Fail<ExamMarkRosterDto>("Mark entry contains an unregistered student or invalid score.", 409);
                normalized.Add((student.RegistrationId, student.StudentId,
                    item.IsAbsent ? 0m : item.ObtainedMark, item.IsAbsent));
            }
            if (normalized.Select(x => x.RegistrationId).Distinct().Count() != normalized.Count)
                return Fail<ExamMarkRosterDto>("A student appears more than once in the mark request.");
            var registrations = normalized.Select(x => x.RegistrationId).ToArray();
            var existing = await _marks.GetQueryable().Where(x => x.TenantId == tenant &&
                x.AssessmentSubjectId == subject.Value.Subject.Id &&
                registrations.Contains(x.StudentSubjectRegistrationId)).ToListAsync(ct);
            var byRegistration = existing.ToDictionary(x => x.StudentSubjectRegistrationId);
            var now = _clock.GetUtcNow().UtcDateTime;
            var gradeRules = await GradeRulesAsync(assessment, subject.Value.Subject, ct);
            foreach (var entry in normalized)
            {
                var grade = GradeFor(gradeRules, subject.Value.Subject.FullMarks == 0m
                    ? 0m : entry.Marks / subject.Value.Subject.FullMarks * 100m);
                if (!byRegistration.TryGetValue(entry.RegistrationId, out var row))
                {
                    row = new StudentAssessmentMark
                    {
                        TenantId = tenant, AssessmentSubjectId = subject.Value.Subject.Id,
                        StudentSubjectRegistrationId = entry.RegistrationId,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _marks.AddAsync(row);
                }
                row.ObtainedMarks = entry.Marks;
                row.IsAbsent = entry.Absent; row.IsWithheld = false;
                row.GradeLetter = grade?.GradeLetter; row.GradePoint = grade?.GradePoint;
                row.EnteredByUserId = _user.UserId; row.EnteredAt = now;
                row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            }
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return ApiResponse<ExamMarkRosterDto>.SuccessResponse(
                await RosterAsync(request, assessment, subject.Value.Subject, subject.Value.Offering, ct),
                "Marks saved.");
        }
        catch (DbUpdateConcurrencyException) { return Fail<ExamMarkRosterDto>("Marks changed concurrently. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent mark write for tenant {TenantId}", tenant);
            return Fail<ExamMarkRosterDto>("Marks conflict with another entry.", 409);
        }
        catch (TransactionAbortedException) { return Fail<ExamMarkRosterDto>("Concurrent mark transaction was rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mark save failed in tenant {TenantId}", tenant);
            return Fail<ExamMarkRosterDto>("Marks could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<ExamResultSheetDto>> GenerateResultsAsync(ExamScopeDto request,
        CancellationToken ct = default)
    {
        if (!CanPublish()) return Fail<ExamResultSheetDto>("Result generation requires a principal or administrator.", 403);
        var loaded = await LoadScopeAsync(request, ct);
        if (loaded.Error != null) return Fail<ExamResultSheetDto>(loaded.Error, loaded.Code);
        var assessment = loaded.Assessment!;
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var publication = await _publications.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.AssessmentId == assessment.Id && x.AcademicBatchId == request.SectionId &&
                x.VersionNo == 1, ct);
            if (publication?.State == ResultPublicationState.Published)
                return Fail<ExamResultSheetDto>("Published results must not be regenerated.", 409);
            if (publication?.State == ResultPublicationState.Withdrawn)
                return Fail<ExamResultSheetDto>("Withdrawn results require a new controlled publication version.", 409);
            if (assessment.State is AssessmentState.Cancelled or AssessmentState.Published)
                return Fail<ExamResultSheetDto>("Assessment is not editable.", 409);
            var subjects = await (from assessmentSubject in _subjects.GetQueryable().AsNoTracking()
                join offering in _offerings.GetQueryable().AsNoTracking()
                    on assessmentSubject.SubjectOfferingId equals offering.Id
                where assessmentSubject.TenantId == tenant && offering.TenantId == tenant &&
                    assessmentSubject.AssessmentId == assessment.Id && offering.AcademicBatchId == request.SectionId
                select new { assessmentSubject, offering }).ToListAsync(ct);
            if (subjects.Count == 0)
                return Fail<ExamResultSheetDto>("Assessment has no subject offerings for the selected batch.", 409);
            var subjectIds = subjects.Select(x => x.assessmentSubject.Id).ToArray();
            var offerings = subjects.Select(x => x.offering.Id).ToArray();
            var students = await EligibleEnrollmentsAsync(request.SectionId, ct);
            if (students.Count == 0) return Fail<ExamResultSheetDto>("No active students in the selected batch.", 409);
            var enrollmentIds = students.Select(x => x.Id).ToArray();
            var registrations = await _registrations.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                enrollmentIds.Contains(x.StudentEnrollmentId) && offerings.Contains(x.SubjectOfferingId) &&
                x.State == SubjectRegistrationState.Approved).ToListAsync(ct);
            var registrationIds = registrations.Select(x => x.Id).ToArray();
            var marks = await _marks.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                subjectIds.Contains(x.AssessmentSubjectId) &&
                registrationIds.Contains(x.StudentSubjectRegistrationId)).ToListAsync(ct);
            var markMap = marks.ToDictionary(x => (x.AssessmentSubjectId, x.StudentSubjectRegistrationId));
            var regMap = registrations.ToDictionary(x => (x.StudentEnrollmentId, x.SubjectOfferingId));
            var gradeRules = await GradeRulesAsync(assessment, null, ct);
            if (gradeRules.Count == 0)
                return Fail<ExamResultSheetDto>("A grade scheme must be configured before generating results.", 409);
            var computed = new List<(StudentEnrollment Enrollment, decimal Marks, decimal Full, decimal Percent,
                decimal Gpa, string? Grade, bool Passed, bool Withheld)>();
            foreach (var enrollment in students)
            {
                decimal marksTotal = 0m, fullTotal = 0m, weighted = 0m, weightTotal = 0m;
                var passed = true; var withheld = false;
                foreach (var subject in subjects)
                {
                    if (!regMap.TryGetValue((enrollment.Id, subject.offering.Id), out var reg) ||
                        !markMap.TryGetValue((subject.assessmentSubject.Id, reg.Id), out var mark))
                        return Fail<ExamResultSheetDto>("Marks and subject registrations must be complete for all students.", 409);
                    marksTotal += mark.ObtainedMarks; fullTotal += subject.assessmentSubject.FullMarks;
                    weighted += mark.ObtainedMarks / subject.assessmentSubject.FullMarks * subject.assessmentSubject.Weightage;
                    weightTotal += subject.assessmentSubject.Weightage;
                    passed &= !mark.IsAbsent && !mark.IsWithheld &&
                        mark.ObtainedMarks >= subject.assessmentSubject.PassMarks;
                    withheld |= mark.IsWithheld;
                }
                var percent = weightTotal <= 0 ? 0m : Math.Round(weighted / weightTotal * 100m, 4);
                var grade = GradeFor(gradeRules, percent);
                if (grade == null)
                    return Fail<ExamResultSheetDto>("No grade rule matches the computed result.", 409);
                computed.Add((enrollment, marksTotal, fullTotal, percent,
                    grade.GradePoint, grade.GradeLetter, passed && !grade.IsFailGrade, withheld));
            }
            publication ??= new ResultPublication
            {
                TenantId = tenant, AssessmentId = assessment.Id, AcademicBatchId = request.SectionId,
                VersionNo = 1, State = ResultPublicationState.Draft,
                CreatedAt = _clock.GetUtcNow().UtcDateTime, CreatedBy = _user.UserId
            };
            if (publication.Id == 0)
            {
                await _publications.AddAsync(publication);
                await _uow.SaveChangesAsync(ct);
            }
            var existingRows = await _summaries.GetQueryable().Where(x => x.TenantId == tenant &&
                x.ResultPublicationId == publication.Id).ToListAsync(ct);
            var byEnrollment = existingRows.ToDictionary(x => x.StudentEnrollmentId);
            var ranked = computed.OrderBy(x => x.Withheld).ThenByDescending(x => x.Passed)
                .ThenByDescending(x => x.Marks).ThenByDescending(x => x.Gpa)
                .ThenBy(x => x.Enrollment.RollNo).ToList();
            var rank = 0;
            var now = _clock.GetUtcNow().UtcDateTime;
            foreach (var item in ranked)
            {
                if (!byEnrollment.TryGetValue(item.Enrollment.Id, out var row))
                {
                    row = new StudentResultSummary
                    {
                        TenantId = tenant, ResultPublicationId = publication.Id,
                        AssessmentId = assessment.Id, StudentEnrollmentId = item.Enrollment.Id,
                        PublicationVersionNo = publication.VersionNo,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _summaries.AddAsync(row);
                }
                row.TotalMarks = item.Full; row.ObtainedMarks = item.Marks;
                row.Percentage = item.Percent; row.GPA = item.Gpa; row.CGPA = null;
                row.GradeLetter = item.Grade; row.MeritPosition = item.Withheld ? null : ++rank;
                row.IsPassed = item.Passed; row.IsWithheld = item.Withheld;
                row.CalculatedAt = now; row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            }
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return await GetResultsInternalAsync(assessment, request, subjects.Count, ct, "Draft results generated.");
        }
        catch (DbUpdateConcurrencyException) { return Fail<ExamResultSheetDto>("Result data changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Result generation conflict for tenant {TenantId}", tenant);
            return Fail<ExamResultSheetDto>("Result generation conflicted with another transaction.", 409);
        }
        catch (TransactionAbortedException) { return Fail<ExamResultSheetDto>("Concurrent result generation rejected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Result generation failed for tenant {TenantId}", tenant);
            return Fail<ExamResultSheetDto>("Results could not be generated.", 500);
        }
    }

    public async Task<ApiResponse<ExamResultSheetDto>> PublishResultsAsync(ExamScopeDto request,
        CancellationToken ct = default)
    {
        if (!CanPublish()) return Fail<ExamResultSheetDto>("Result publication requires a principal or administrator.", 403);
        var loaded = await LoadScopeAsync(request, ct);
        if (loaded.Error != null) return Fail<ExamResultSheetDto>(loaded.Error, loaded.Code);
        var assessment = loaded.Assessment!;
        var tenant = _user.TenantId;
        try
        {
            using var tx = SerializableScope();
            var publication = await _publications.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.AssessmentId == request.ExamId &&
                x.AcademicBatchId == request.SectionId && x.VersionNo == 1, ct);
            if (publication == null) return Fail<ExamResultSheetDto>("Generate complete results before publishing.", 409);
            if (publication.State == ResultPublicationState.Withdrawn)
                return Fail<ExamResultSheetDto>("Withdrawn results cannot be republished.", 409);
            if (publication.State == ResultPublicationState.Published)
            {
                tx.Complete();
                return await GetResultsInternalAsync(assessment, request, 0, ct, "Results were already published.");
            }
            var ids = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.AcademicBatchId == request.SectionId && x.IsCurrent && x.IsActive)
                .Select(x => x.Id).ToListAsync(ct);
            var resultIds = await _summaries.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.ResultPublicationId == publication.Id).Select(x => x.StudentEnrollmentId).ToListAsync(ct);
            if (ids.Count == 0 || resultIds.Count != ids.Count || ids.Except(resultIds).Any())
                return Fail<ExamResultSheetDto>("Results are incomplete for the current batch roster.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            publication.State = ResultPublicationState.Published;
            publication.PublishedAt = now; publication.PublishedByUserId = _user.UserId;
            publication.UpdatedAt = now; publication.UpdatedBy = _user.UserId;
            var subjectBatchIds = await (from subject in _subjects.GetQueryable().AsNoTracking()
                join offering in _offerings.GetQueryable().AsNoTracking()
                    on subject.SubjectOfferingId equals offering.Id
                where subject.TenantId == tenant && offering.TenantId == tenant &&
                    subject.AssessmentId == assessment.Id
                select offering.AcademicBatchId).Distinct().ToArrayAsync(ct);
            var publishedBatchIds = await _publications.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && x.AssessmentId == assessment.Id &&
                    x.State == ResultPublicationState.Published)
                .Select(x => x.AcademicBatchId).ToListAsync(ct);
            if (subjectBatchIds.All(x => x == request.SectionId || publishedBatchIds.Contains(x)))
            {
                var liveAssessment = await _assessments.GetQueryable().FirstAsync(x =>
                    x.TenantId == tenant && x.Id == assessment.Id, ct);
                liveAssessment.State = AssessmentState.Published;
                liveAssessment.UpdatedAt = now; liveAssessment.UpdatedBy = _user.UserId;
            }
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return await GetResultsInternalAsync(assessment, request, 0, ct, "Results published.");
        }
        catch (DbUpdateConcurrencyException) { return Fail<ExamResultSheetDto>("Publication changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Result publication conflict for tenant {TenantId}", tenant);
            return Fail<ExamResultSheetDto>("Publication conflicts with another transaction.", 409);
        }
        catch (TransactionAbortedException) { return Fail<ExamResultSheetDto>("Concurrent result publication rejected.", 409); }
    }

    public async Task<ApiResponse<ExamResultSheetDto>> GetResultsAsync(ExamScopeDto request,
        CancellationToken ct = default)
    {
        if (!CanPublish()) return Fail<ExamResultSheetDto>("Class result-sheet access requires an administrator or principal.", 403);
        var loaded = await LoadScopeAsync(request, ct);
        if (loaded.Error != null) return Fail<ExamResultSheetDto>(loaded.Error, loaded.Code);
        return await GetResultsInternalAsync(loaded.Assessment!, request, 0, ct);
    }

    private async Task<(Assessment? Assessment, AcademicBatch? Batch, string? Error, int Code)> LoadScopeAsync(
        ExamScopeDto request, CancellationToken ct)
    {
        if (request == null || request.ExamId <= 0 || request.ClassId <= 0 || request.SectionId <= 0)
            return (null, null, "Assessment reference, level and batch are required.", 400);
        var t = _user.TenantId;
        var assessment = await _assessments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == t && x.Id == request.ExamId, ct);
        if (assessment == null) return (null, null, "Assessment not found.", 404);
        var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == t &&
            x.Id == request.SectionId && x.AcademicLevelId == request.ClassId &&
            x.AcademicYearId == assessment.AcademicYearId && x.CampusId == assessment.CampusId &&
            x.IsActive, ct);
        if (batch == null) return (null, null, "Selected batch is not within the assessment scope.", 409);
        return (assessment, batch, null, 200);
    }

    private async Task<(AssessmentSubject Subject, SubjectOffering Offering)?> LoadSubjectAsync(
        ExamMarkRosterQueryDto request, CancellationToken ct)
    {
        var t = _user.TenantId;
        var list = await (from assessmentSubject in _subjects.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on assessmentSubject.SubjectOfferingId equals offering.Id
            join curriculum in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals curriculum.Id
            where assessmentSubject.TenantId == t && offering.TenantId == t && curriculum.TenantId == t &&
                assessmentSubject.AssessmentId == request.ExamId &&
                offering.AcademicBatchId == request.SectionId && curriculum.SubjectId == request.SubjectId
            select new { Subject = assessmentSubject, Offering = offering }).Take(2).ToListAsync(ct);
        if (list.Count != 1) return null;
        return (list[0].Subject, list[0].Offering);
    }

    private async Task<bool> CanEnterMarksAsync(long offeringId, CancellationToken ct)
    {
        if (CanPublish()) return true;
        if (!_user.IsInRole("Teacher")) return false;
        var t = _user.TenantId;
        var teacher = await _employees.GetQueryable().AsNoTracking().Where(x => x.TenantId == t &&
            x.UserId == _user.UserId && x.CanTeach && x.State == EmployeeState.Active)
            .Select(x => x.Id).FirstOrDefaultAsync(ct);
        return teacher != 0 && await _instructors.GetQueryable().AsNoTracking().AnyAsync(x =>
            x.TenantId == t && x.SubjectOfferingId == offeringId && x.EmployeeId == teacher && x.IsActive, ct);
    }

    private async Task<List<EligibleRow>> EligibleAsync(long offeringId, long batchId, CancellationToken ct)
    {
        var t = _user.TenantId;
        return await (from reg in _registrations.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on reg.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking() on enrollment.StudentId equals student.Id
            where reg.TenantId == t && enrollment.TenantId == t && student.TenantId == t &&
                reg.SubjectOfferingId == offeringId && reg.State == SubjectRegistrationState.Approved &&
                enrollment.AcademicBatchId == batchId && enrollment.IsCurrent && enrollment.IsActive &&
                student.IsActive
            orderby enrollment.RollNo, student.Id
            select new EligibleRow
            {
                RegistrationId = reg.Id, EnrollmentId = enrollment.Id, StudentId = student.Id,
                StudentReference = student.PublicId, StudentCode = student.StudentCode,
                StudentName = student.FullName, Roll = enrollment.RollNo
            }).ToListAsync(ct);
    }

    private async Task<List<StudentEnrollment>> EligibleEnrollmentsAsync(long batchId, CancellationToken ct)
    {
        var t = _user.TenantId;
        return await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == t &&
            x.AcademicBatchId == batchId && x.IsCurrent && x.IsActive &&
            x.State == EnrollmentState.Active).OrderBy(x => x.RollNo).ToListAsync(ct);
    }

    private async Task<ExamMarkRosterDto> RosterAsync(ExamMarkRosterQueryDto request, Assessment exam,
        AssessmentSubject assessmentSubject, SubjectOffering offering, CancellationToken ct)
    {
        var students = await EligibleAsync(offering.Id, request.SectionId, ct);
        var ids = students.Select(x => x.RegistrationId).ToArray();
        var marks = await _marks.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.AssessmentSubjectId == assessmentSubject.Id && ids.Contains(x.StudentSubjectRegistrationId))
            .ToDictionaryAsync(x => x.StudentSubjectRegistrationId, ct);
        var curriculumName = await (from entry in _curriculumSubjects.GetQueryable().AsNoTracking()
            join subject in _subjectsForNames() on entry.SubjectId equals subject.Id
            where entry.TenantId == _user.TenantId && entry.Id == offering.CurriculumSubjectId
            select subject.Name).FirstOrDefaultAsync(ct);
        var published = await _publications.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == _user.TenantId &&
            x.AssessmentId == exam.Id && x.AcademicBatchId == request.SectionId &&
            x.State == ResultPublicationState.Published, ct);
        return new ExamMarkRosterDto
        {
            ExamId = exam.Id, ExamName = exam.Name, ClassId = request.ClassId,
            SectionId = request.SectionId, SubjectId = request.SubjectId,
            SubjectName = curriculumName ?? "",
            FullMark = DecimalAsInt(assessmentSubject.FullMarks),
            PassMark = DecimalAsInt(assessmentSubject.PassMarks),
            IsResultPublished = published,
            Students = students.Select(x =>
            {
                marks.TryGetValue(x.RegistrationId, out var mark);
                return new ExamMarkRosterItemDto
                {
                    StudentId = x.StudentId, StudentReference = x.StudentReference,
                    StudentCode = x.StudentCode, Roll = x.Roll, StudentName = x.StudentName,
                    ObtainedMark = mark?.ObtainedMarks, IsAbsent = mark?.IsAbsent ?? false,
                    Grade = mark?.GradeLetter, GPA = mark?.GradePoint
                };
            }).ToList()
        };
    }

    private IQueryable<EduOS.Core.Entities.Academic.Subject> _subjectsForNames() =>
        _subjectNames.GetQueryable().AsNoTracking();

    private async Task<List<GradeRule>> GradeRulesAsync(Assessment exam, AssessmentSubject? subject, CancellationToken ct)
    {
        var schemeId = subject?.GradeSchemeId ?? exam.GradeSchemeId;
        if (!schemeId.HasValue) return new List<GradeRule>();
        return await _grades.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.GradeSchemeId == schemeId.Value).OrderByDescending(x => x.MinMarks).ToListAsync(ct);
    }
    private static GradeRule? GradeFor(IEnumerable<GradeRule> rules, decimal percent) =>
        rules.FirstOrDefault(x => percent >= x.MinMarks && percent <= x.MaxMarks);
    private async Task<ExamResultSheetDto> ReadSheetAsync(Assessment exam, ExamScopeDto request,
        int subjectCount, CancellationToken ct)
    {
        var pub = await _publications.GetQueryable().AsNoTracking().Where(x => x.TenantId == _user.TenantId &&
            x.AssessmentId == exam.Id && x.AcademicBatchId == request.SectionId)
            .OrderByDescending(x => x.VersionNo).FirstOrDefaultAsync(ct);
        if (pub == null) return new ExamResultSheetDto
        {
            ExamId = exam.Id, ExamName = exam.Name, AcademicYearId = exam.AcademicYearId,
            ClassId = request.ClassId, SectionId = request.SectionId, SubjectCount = subjectCount
        };
        var rows = await (from row in _summaries.GetQueryable().AsNoTracking()
            join enrollment in _enrollments.GetQueryable().AsNoTracking()
                on row.StudentEnrollmentId equals enrollment.Id
            join student in _students.GetQueryable().AsNoTracking()
                on enrollment.StudentId equals student.Id
            where row.TenantId == _user.TenantId && enrollment.TenantId == _user.TenantId &&
                student.TenantId == _user.TenantId && row.ResultPublicationId == pub.Id
            orderby row.MeritPosition, enrollment.RollNo
            select new { row, enrollment, student }).Take(5000).ToListAsync(ct);
        return new ExamResultSheetDto
        {
            ExamId = exam.Id, ExamName = exam.Name, AcademicYearId = exam.AcademicYearId,
            ClassId = request.ClassId, SectionId = request.SectionId, SubjectCount = subjectCount,
            Results = rows.Select(x => new ExamResultItemDto
            {
                StudentId = x.student.Id, StudentReference = x.student.PublicId,
                StudentCode = x.student.StudentCode, StudentName = x.student.FullName,
                Roll = x.enrollment.RollNo,
                TotalMark = x.row.ObtainedMarks, TotalFullMark = x.row.TotalMarks,
                Percentage = x.row.Percentage ?? 0m, TotalGPA = x.row.GPA ?? 0m,
                FinalGrade = x.row.GradeLetter, Position = x.row.MeritPosition,
                IsPassed = x.row.IsPassed, IsPublished = pub.State == ResultPublicationState.Published,
                PublishedAtUtc = pub.PublishedAt
            }).ToList()
        };
    }
    private async Task<ApiResponse<ExamResultSheetDto>> GetResultsInternalAsync(Assessment exam,
        ExamScopeDto request, int subjectCount, CancellationToken ct, string? message = null)
    {
        var result = await ReadSheetAsync(exam, request, subjectCount, ct);
        return ApiResponse<ExamResultSheetDto>.SuccessResponse(result, message);
    }

    private sealed class EligibleRow
    {
        public long RegistrationId { get; set; }
        public long EnrollmentId { get; set; }
        public long StudentId { get; set; }
        public Guid StudentReference { get; set; }
        public string StudentCode { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string Roll { get; set; } = "";
    }
    private static int DecimalAsInt(decimal value) => decimal.ToInt32(decimal.Truncate(value));
    private static TransactionScope SerializableScope() => new(TransactionScopeOption.Required,
        new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
    private bool CanAccess() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (CanPublish() || _user.IsInRole("Teacher"));
    private bool CanPublish() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal"));
    private static ApiResponse<T> Fail<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
}
