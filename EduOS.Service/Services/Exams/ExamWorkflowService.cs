using EduOS.Core.Common;
using EduOS.Core.DTOs.Exams;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Exams;

public sealed class ExamWorkflowService : IExamWorkflowService
{
    private readonly IGenericRepository<Exam> _exams;
    private readonly IGenericRepository<ExamSchedule> _schedules;
    private readonly IGenericRepository<MarkEntry> _marks;
    private readonly IGenericRepository<ExamResult> _results;
    private readonly IGenericRepository<GradeRule> _gradeRules;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<Section> _sections;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<ExamWorkflowService> _logger;

    public ExamWorkflowService(IGenericRepository<Exam> exams, IGenericRepository<ExamSchedule> schedules,
        IGenericRepository<MarkEntry> marks, IGenericRepository<ExamResult> results, IGenericRepository<GradeRule> gradeRules,
        IGenericRepository<Enrollment> enrollments, IGenericRepository<Section> sections, IUnitOfWork unitOfWork,
        ICurrentUserService currentUser, TimeProvider clock, ILogger<ExamWorkflowService> logger)
    {
        _exams = exams; _schedules = schedules; _marks = marks; _results = results; _gradeRules = gradeRules;
        _enrollments = enrollments; _sections = sections; _unitOfWork = unitOfWork; _currentUser = currentUser;
        _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<ExamMarkRosterDto>> GetMarkRosterAsync(ExamMarkRosterQueryDto request, CancellationToken cancellationToken = default)
    {
        if (!CanMark()) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Exam access is required.", 403);
        var context = await LoadMarkContextAsync(request, cancellationToken);
        if (context.Error != null) return ApiResponse<ExamMarkRosterDto>.ErrorResponse(context.Error, context.StatusCode);
        return ApiResponse<ExamMarkRosterDto>.SuccessResponse(await BuildMarkRosterAsync(context.Exam!, context.Schedule!, context.Enrollments!, request, cancellationToken));
    }

    public async Task<ApiResponse<ExamMarkRosterDto>> SaveMarksAsync(SaveExamMarksDto request, CancellationToken cancellationToken = default)
    {
        if (!CanMark()) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Exam mark-entry access is required.", 403);
        if (request.Items.Count == 0 || request.Items.GroupBy(x => x.StudentId).Any(g => g.Count() > 1)) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Mark entries are invalid.");
        var context = await LoadMarkContextAsync(request, cancellationToken);
        if (context.Error != null) return ApiResponse<ExamMarkRosterDto>.ErrorResponse(context.Error, context.StatusCode);
        var exam = context.Exam!; var schedule = context.Schedule!; var enrollments = context.Enrollments!;
        var enrolledIds = enrollments.Select(x => x.StudentId).ToHashSet();
        if (request.Items.Any(x => !enrolledIds.Contains(x.StudentId))) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("One or more students are not active in the selected class and section.", 409);
        if (request.Items.Any(x => !x.IsAbsent && x.ObtainedMark > schedule.FullMark)) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Obtained mark cannot exceed the scheduled full mark.");
        var studentIds = request.Items.Select(x => x.StudentId).ToArray();
        if (await _results.AnyAsync(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.IsPublished && studentIds.Contains(x.StudentId))) return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Published results cannot be edited.", 409);

        try
        {
            var rules = await _gradeRules.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId).OrderBy(x => x.MinMark).ToListAsync(cancellationToken);
            var existing = await _marks.GetQueryable().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.SubjectId == request.SubjectId && studentIds.Contains(x.StudentId)).ToListAsync(cancellationToken);
            var byStudent = existing.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.First());
            var now = _clock.GetUtcNow().UtcDateTime;
            foreach (var item in request.Items)
            {
                var mark = item.IsAbsent ? 0m : item.ObtainedMark;
                var percentage = schedule.FullMark <= 0 ? 0 : mark / schedule.FullMark * 100m;
                var rule = FindGrade(rules, percentage);
                if (!byStudent.TryGetValue(item.StudentId, out var entity))
                {
                    entity = new MarkEntry { TenantId = _currentUser.TenantId, ExamId = exam.Id > int.MaxValue ? request.ExamId : (int)exam.Id, StudentId = item.StudentId, SubjectId = request.SubjectId, CreatedAt = now, CreatedBy = _currentUser.UserId };
                    await _marks.AddAsync(entity);
                }
                entity.ObtainedMark = mark; entity.FullMark = schedule.FullMark; entity.IsAbsent = item.IsAbsent;
                entity.Grade = rule?.Grade; entity.GPA = rule?.GPA; entity.EnteredBy = _currentUser.UserId; entity.EntryDate = now;
                entity.UpdatedAt = now; entity.UpdatedBy = _currentUser.UserId;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<ExamMarkRosterDto>.SuccessResponse(await BuildMarkRosterAsync(exam, schedule, enrollments, request, cancellationToken), "Marks saved successfully.");
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting mark entry for tenant {TenantId}, exam {ExamId}, subject {SubjectId}", _currentUser.TenantId, request.ExamId, request.SubjectId);
            return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Marks conflict with another update. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Mark entry failed for tenant {TenantId}, exam {ExamId}, subject {SubjectId}", _currentUser.TenantId, request.ExamId, request.SubjectId);
            return ApiResponse<ExamMarkRosterDto>.ErrorResponse("Marks could not be saved.", 500);
        }
    }

    public async Task<ApiResponse<ExamResultSheetDto>> GenerateResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return ApiResponse<ExamResultSheetDto>.ErrorResponse("Exam result access is required.", 403);
        var scope = await LoadResultScopeAsync(request, cancellationToken);
        if (scope.Error != null) return ApiResponse<ExamResultSheetDto>.ErrorResponse(scope.Error, scope.StatusCode);
        var exam = scope.Exam!; var schedules = scope.Schedules!; var enrollments = scope.Enrollments!;
        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToArray();
        if (studentIds.Length == 0) return ApiResponse<ExamResultSheetDto>.ErrorResponse("No active students were found for this section.", 409);
        if (await _results.AnyAsync(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && x.IsPublished)) return ApiResponse<ExamResultSheetDto>.ErrorResponse("Published results cannot be regenerated.", 409);

        try
        {
            var subjectIds = schedules.Select(x => x.SubjectId).Distinct().ToArray();
            var marks = await _marks.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && studentIds.Contains(x.StudentId) && subjectIds.Contains(x.SubjectId)).ToListAsync(cancellationToken);
            var markLookup = marks.GroupBy(x => x.StudentId).ToDictionary(g => g.Key, g => g.GroupBy(x => x.SubjectId).ToDictionary(s => s.Key, s => s.OrderByDescending(x => x.Id).First()));
            var incomplete = enrollments.Count(e => !markLookup.TryGetValue(e.StudentId, out var map) || subjectIds.Any(s => !map.ContainsKey(s)));
            if (incomplete > 0) return ApiResponse<ExamResultSheetDto>.ErrorResponse($"Marks are incomplete for {incomplete} student(s).", 409);

            var rules = await _gradeRules.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId).OrderBy(x => x.MinMark).ToListAsync(cancellationToken);
            var existing = await _results.GetQueryable().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && studentIds.Contains(x.StudentId)).ToListAsync(cancellationToken);
            var byStudent = existing.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.First());
            var computed = new List<(Enrollment Enrollment, decimal Total, decimal Full, decimal Gpa, decimal Percentage, string? Grade, bool Passed)>();
            foreach (var enrollment in enrollments.GroupBy(x => x.StudentId).Select(x => x.First()))
            {
                var studentMarks = markLookup[enrollment.StudentId];
                var total = subjectIds.Sum(id => studentMarks[id].ObtainedMark);
                var full = schedules.GroupBy(x => x.SubjectId).Select(x => x.First()).Sum(x => (decimal)x.FullMark);
                var passed = schedules.GroupBy(x => x.SubjectId).Select(x => x.First()).All(s => !studentMarks[s.SubjectId].IsAbsent && studentMarks[s.SubjectId].ObtainedMark >= s.PassMark);
                var gpa = subjectIds.Average(id => studentMarks[id].GPA ?? 0m);
                var percentage = full <= 0 ? 0 : Math.Round(total / full * 100m, 2);
                computed.Add((enrollment, total, full, Math.Round(gpa, 2), percentage, FindGrade(rules, percentage)?.Grade, passed));
            }
            var ranked = computed.OrderByDescending(x => x.Passed).ThenByDescending(x => x.Total).ThenByDescending(x => x.Gpa).ThenBy(x => x.Enrollment.Roll).ToList();
            var now = _clock.GetUtcNow().UtcDateTime;
            for (var i = 0; i < ranked.Count; i++)
            {
                var row = ranked[i];
                if (!byStudent.TryGetValue(row.Enrollment.StudentId, out var result))
                {
                    result = new ExamResult { TenantId = _currentUser.TenantId, ExamId = request.ExamId, StudentId = row.Enrollment.StudentId, CreatedAt = now, CreatedBy = _currentUser.UserId };
                    await _results.AddAsync(result);
                }
                result.AcademicYearId = exam.AcademicYearId; result.ClassId = request.ClassId; result.SectionId = request.SectionId; result.GroupId = row.Enrollment.GroupId;
                result.TotalMark = row.Total; result.TotalFullMark = row.Full; result.Percentage = row.Percentage; result.TotalGPA = row.Gpa; result.FinalGrade = row.Grade;
                result.Position = i + 1; result.IsPassed = row.Passed; result.IsPublished = false; result.PublishedAtUtc = null; result.PublishedByUserId = null; result.UpdatedAt = now; result.UpdatedBy = _currentUser.UserId;
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return await GetResultsInternalAsync(exam, request, schedules.Count, cancellationToken, "Results generated for review.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Result generation failed for tenant {TenantId}, exam {ExamId}, class {ClassId}, section {SectionId}", _currentUser.TenantId, request.ExamId, request.ClassId, request.SectionId);
            return ApiResponse<ExamResultSheetDto>.ErrorResponse("Results could not be generated.", 500);
        }
    }

    public async Task<ApiResponse<ExamResultSheetDto>> PublishResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default)
    {
        if (!CanPublish()) return ApiResponse<ExamResultSheetDto>.ErrorResponse("Exam publishing access is required.", 403);
        var scope = await LoadResultScopeAsync(request, cancellationToken);
        if (scope.Error != null) return ApiResponse<ExamResultSheetDto>.ErrorResponse(scope.Error, scope.StatusCode);
        var exam = scope.Exam!; var schedules = scope.Schedules!; var enrollments = scope.Enrollments!;
        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToArray();
        var results = await _results.GetQueryable().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && studentIds.Contains(x.StudentId)).ToListAsync(cancellationToken);
        if (results.Count != studentIds.Length) return ApiResponse<ExamResultSheetDto>.ErrorResponse("Generate complete results before publishing.", 409);
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var result in results) { result.IsPublished = true; result.PublishedAtUtc = now; result.PublishedByUserId = _currentUser.UserId; result.UpdatedAt = now; result.UpdatedBy = _currentUser.UserId; }
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var scheduledClassIds = await _schedules.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId).Select(x => x.ClassId).Distinct().ToListAsync(cancellationToken);
        var expectedIds = await _enrollments.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.IsActive && x.AcademicYearId == exam.AcademicYearId && scheduledClassIds.Contains(x.ClassId) && x.Student != null && x.Student.IsActive).Select(x => x.StudentId).Distinct().ToListAsync(cancellationToken);
        var publishedIds = await _results.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.IsPublished).Select(x => x.StudentId).Distinct().ToListAsync(cancellationToken);
        exam.IsPublished = expectedIds.Count > 0 && expectedIds.All(x => publishedIds.Contains(x)); exam.UpdatedAt = now; exam.UpdatedBy = _currentUser.UserId;
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return await GetResultsInternalAsync(exam, request, schedules.Count, cancellationToken, "Results published successfully.");
    }

    public async Task<ApiResponse<ExamResultSheetDto>> GetResultsAsync(ExamScopeDto request, CancellationToken cancellationToken = default)
    {
        if (!CanMark()) return ApiResponse<ExamResultSheetDto>.ErrorResponse("Exam access is required.", 403);
        var scope = await LoadResultScopeAsync(request, cancellationToken);
        if (scope.Error != null) return ApiResponse<ExamResultSheetDto>.ErrorResponse(scope.Error, scope.StatusCode);
        return await GetResultsInternalAsync(scope.Exam!, request, scope.Schedules!.Count, cancellationToken);
    }

    private async Task<(Exam? Exam, ExamSchedule? Schedule, List<Enrollment>? Enrollments, string? Error, int StatusCode)> LoadMarkContextAsync(ExamMarkRosterQueryDto request, CancellationToken cancellationToken)
    {
        var exam = await _exams.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.ExamId && x.IsActive, cancellationToken);
        if (exam == null) return (null, null, null, "Exam is unavailable.", 404);
        if (!await _sections.AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.SectionId && x.ClassId == request.ClassId && x.IsActive)) return (null, null, null, "Section is unavailable for the selected class.", 409);
        var schedule = await _schedules.GetQueryable().AsNoTracking().Include(x => x.Subject).FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId && x.SubjectId == request.SubjectId, cancellationToken);
        if (schedule == null) return (null, null, null, "Subject is not scheduled for this exam and class.", 409);
        var enrollments = await _enrollments.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == _currentUser.TenantId && x.IsActive && x.AcademicYearId == exam.AcademicYearId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && x.Student != null && x.Student.IsActive).OrderBy(x => x.Roll).ToListAsync(cancellationToken);
        return (exam, schedule, enrollments, null, 200);
    }

    private async Task<(Exam? Exam, List<ExamSchedule>? Schedules, List<Enrollment>? Enrollments, string? Error, int StatusCode)> LoadResultScopeAsync(ExamScopeDto request, CancellationToken cancellationToken)
    {
        var exam = await _exams.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.ExamId && x.IsActive, cancellationToken);
        if (exam == null) return (null, null, null, "Exam is unavailable.", 404);
        if (!await _sections.AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.SectionId && x.ClassId == request.ClassId && x.IsActive)) return (null, null, null, "Section is unavailable for the selected class.", 409);
        var schedules = await _schedules.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId).OrderBy(x => x.ExamDate).ThenBy(x => x.SubjectId).ToListAsync(cancellationToken);
        if (schedules.Count == 0) return (null, null, null, "No exam schedule was found for the selected class.", 409);
        var enrollments = await _enrollments.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == _currentUser.TenantId && x.IsActive && x.AcademicYearId == exam.AcademicYearId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && x.Student != null && x.Student.IsActive).OrderBy(x => x.Roll).ToListAsync(cancellationToken);
        return (exam, schedules, enrollments, null, 200);
    }

    private async Task<ExamMarkRosterDto> BuildMarkRosterAsync(Exam exam, ExamSchedule schedule, List<Enrollment> enrollments, ExamMarkRosterQueryDto request, CancellationToken cancellationToken)
    {
        var studentIds = enrollments.Select(x => x.StudentId).Distinct().ToArray();
        var marks = studentIds.Length == 0 ? new List<MarkEntry>() : await _marks.GetQueryable().AsNoTracking().Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.SubjectId == request.SubjectId && studentIds.Contains(x.StudentId)).ToListAsync(cancellationToken);
        var byStudent = marks.GroupBy(x => x.StudentId).ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.Id).First());
        return new ExamMarkRosterDto
        {
            ExamId = request.ExamId, ExamName = exam.Name, ClassId = request.ClassId, SectionId = request.SectionId, SubjectId = request.SubjectId,
            SubjectName = schedule.Subject?.Name ?? string.Empty, FullMark = schedule.FullMark, PassMark = schedule.PassMark,
            IsResultPublished = await _results.AnyAsync(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId && x.SectionId == request.SectionId && x.IsPublished),
            Students = enrollments.GroupBy(x => x.StudentId).Select(x => x.First()).Select(x => { byStudent.TryGetValue(x.StudentId, out var m); return new ExamMarkRosterItemDto { StudentId = x.StudentId, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentCode = x.Student?.StudentCode ?? string.Empty, Roll = x.Roll, StudentName = x.Student?.FullName ?? string.Empty, ObtainedMark = m?.ObtainedMark, IsAbsent = m?.IsAbsent ?? false, Grade = m?.Grade, GPA = m?.GPA }; }).ToList()
        };
    }

    private async Task<ApiResponse<ExamResultSheetDto>> GetResultsInternalAsync(Exam exam, ExamScopeDto request, int subjectCount, CancellationToken cancellationToken, string? message = null)
    {
        var rows = await _results.GetQueryable().AsNoTracking().Include(x => x.Student).Where(x => x.TenantId == _currentUser.TenantId && x.ExamId == request.ExamId && x.ClassId == request.ClassId && x.SectionId == request.SectionId).OrderBy(x => x.Position).ThenBy(x => x.Student!.Roll).ToListAsync(cancellationToken);
        return ApiResponse<ExamResultSheetDto>.SuccessResponse(new ExamResultSheetDto { ExamId = request.ExamId, ExamName = exam.Name, AcademicYearId = exam.AcademicYearId, ClassId = request.ClassId, SectionId = request.SectionId, SubjectCount = subjectCount, Results = rows.Select(x => new ExamResultItemDto { StudentId = x.StudentId, StudentReference = x.Student?.PublicId ?? Guid.Empty, StudentCode = x.Student?.StudentCode ?? string.Empty, Roll = x.Student?.Roll ?? string.Empty, StudentName = x.Student?.FullName ?? string.Empty, TotalMark = x.TotalMark, TotalFullMark = x.TotalFullMark, Percentage = x.Percentage, TotalGPA = x.TotalGPA, FinalGrade = x.FinalGrade, Position = x.Position, IsPassed = x.IsPassed, IsPublished = x.IsPublished, PublishedAtUtc = x.PublishedAtUtc }).ToList() }, message);
    }

    private bool CanMark() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal") || _currentUser.IsInRole("Teacher") || _currentUser.IsInRole("ExamController"));
    private bool CanPublish() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal") || _currentUser.IsInRole("VicePrincipal") || _currentUser.IsInRole("ExamController"));
    private static GradeRule? FindGrade(IEnumerable<GradeRule> rules, decimal percentage) => rules.FirstOrDefault(x => percentage >= x.MinMark && percentage <= x.MaxMark);
}
