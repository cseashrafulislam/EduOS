using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Students;

public sealed class StudentPromotionService : IStudentPromotionService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<StudentPromotionRecord> _records;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<Class> _classes;
    private readonly IGenericRepository<Section> _sections;
    private readonly IGenericRepository<Group> _groups;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<StudentPromotionService> _logger;

    public StudentPromotionService(
        IGenericRepository<Student> students,
        IGenericRepository<Enrollment> enrollments,
        IGenericRepository<StudentPromotionRecord> records,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<Class> classes,
        IGenericRepository<Section> sections,
        IGenericRepository<Group> groups,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<StudentPromotionService> logger)
    {
        _students = students;
        _enrollments = enrollments;
        _records = records;
        _academicYears = academicYears;
        _classes = classes;
        _sections = sections;
        _groups = groups;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync(
        Guid studentReference,
        PromoteStudentRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<StudentPromotionResultDto>();
        if (studentReference == Guid.Empty || !IsValid(request))
            return Error("Promotion request is invalid.");
        if (!TryVersion(request.StudentRowVersion, out var expectedStudentVersion)
            || !TryVersion(request.SourceEnrollmentRowVersion, out var expectedEnrollmentVersion))
        {
            return Error("Row version is invalid.");
        }

        var tenantId = _currentUser.TenantId;
        var targetRoll = request.TargetRoll.Trim();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        if (!TryLegacyId(request.TargetAcademicYearId, out var targetYearId)
            || !TryLegacyId(request.TargetClassId, out var targetClassId)
            || !TryLegacyId(request.TargetSectionId, out var targetSectionId)
            || !TryNullableLegacyId(request.TargetGroupId, out var targetGroupId))
        {
            return Error("Promotion target is invalid.");
        }

        try
        {
            var student = await _students.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == studentReference,
                    cancellationToken);
            if (student == null) return Error("Student not found.", 404);
            if (student.Id > int.MaxValue) return Error("Student cannot use the legacy academic placement model.", 409);

            var existingByClient = await _records.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                          && x.ClientRequestId == request.ClientRequestId,
                    cancellationToken);
            if (existingByClient != null)
            {
                if (existingByClient.StudentId != student.Id)
                    return Error("Client request reference was already used.", 409);
                return await ExistingResultAsync(existingByClient, student, cancellationToken);
            }

            if (!student.IsActive || !string.Equals(student.Status, "Active", StringComparison.OrdinalIgnoreCase))
                return Error("Only an active student can be promoted.", 409);
            if (!VersionsMatch(student.RowVersion, expectedStudentVersion))
                return Error("The student was changed by another user. Reload and try again.", 409);

            var studentId = checked((int)student.Id);
            var source = await _enrollments.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                          && x.Id == request.SourceEnrollmentId
                                          && x.StudentId == studentId,
                    cancellationToken);
            if (source == null) return Error("Source enrollment not found.", 404);
            if (!source.IsActive) return Error("Source enrollment is no longer active.", 409);
            if (!VersionsMatch(source.RowVersion, expectedEnrollmentVersion))
                return Error("The enrollment was changed by another user. Reload and try again.", 409);
            if (!MatchesCurrentPlacement(student, source))
                return Error("Student placement and active enrollment are inconsistent.", 409);

            var existingFromSource = await _records.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId
                                          && x.FromEnrollmentId == source.Id,
                    cancellationToken);
            if (existingFromSource != null)
            {
                if (MatchesRequest(existingFromSource, request, targetRoll))
                    return await ExistingResultAsync(existingFromSource, student, cancellationToken);
                return Error("Source enrollment was already progressed.", 409);
            }

            var sourceYear = await _academicYears.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == source.AcademicYearId && x.IsActive);
            var targetYear = await _academicYears.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == request.TargetAcademicYearId && x.IsActive);
            var sourceClass = await _classes.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == source.ClassId && x.IsActive);
            var targetClass = await _classes.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == request.TargetClassId && x.IsActive);
            var targetSection = await _sections.FirstOrDefaultAsync(x =>
                x.TenantId == tenantId
                && x.Id == request.TargetSectionId
                && x.ClassId == targetClassId
                && x.IsActive);
            if (sourceYear == null || targetYear == null || sourceClass == null
                || targetClass == null || targetSection == null)
            {
                return Error("Promotion target is unavailable.", 409);
            }

            if (targetYear.Id == sourceYear.Id || targetYear.StartDate <= sourceYear.StartDate)
                return Error("Target academic year must be later than the source year.", 409);
            if (request.Decision == StudentProgressionDecision.Promoted
                && targetClass.NumericValue <= sourceClass.NumericValue)
            {
                return Error("A promotion must move to a higher academic unit.", 409);
            }
            if (request.Decision == StudentProgressionDecision.Repeated
                && targetClass.Id != sourceClass.Id)
            {
                return Error("A repeated student must remain in the same academic unit.", 409);
            }

            Group? targetGroup = null;
            if (request.TargetGroupId.HasValue)
            {
                targetGroup = await _groups.FirstOrDefaultAsync(x =>
                    x.TenantId == tenantId && x.Id == request.TargetGroupId.Value && x.IsActive);
                if (targetGroup == null) return Error("Target group is unavailable.", 409);
            }

            if (await _enrollments.AnyAsync(x => x.TenantId == tenantId
                                                  && x.StudentId == studentId
                                                  && x.AcademicYearId == targetYearId))
            {
                return Error("The student already has an enrollment in the target year.", 409);
            }
            if (await _enrollments.AnyAsync(x => x.TenantId == tenantId
                                                  && x.AcademicYearId == targetYearId
                                                  && x.ClassId == targetClassId
                                                  && x.SectionId == targetSectionId
                                                  && x.Roll == targetRoll
                                                  && x.IsActive))
            {
                return Error("Target roll is already assigned in the section.", 409);
            }
            if (targetSection.Capacity > 0)
            {
                var occupied = await _enrollments.CountAsync(x => x.TenantId == tenantId
                                                                  && x.AcademicYearId == targetYearId
                                                                  && x.ClassId == targetClassId
                                                                  && x.SectionId == targetSectionId
                                                                  && x.IsActive);
                if (occupied >= targetSection.Capacity)
                    return Error("Target section has reached capacity.", 409);
            }

            var now = _clock.GetUtcNow().UtcDateTime;
            source.IsActive = false;
            var target = new Enrollment
            {
                TenantId = tenantId,
                StudentId = studentId,
                AcademicYearId = targetYearId,
                ClassId = targetClassId,
                SectionId = targetSectionId,
                GroupId = targetGroupId,
                CampusId = source.CampusId,
                AcademicTermId = null,
                Roll = targetRoll,
                EnrollmentDate = now,
                IsActive = true
            };
            await _enrollments.AddAsync(target);

            student.AcademicYearId = targetYearId;
            student.ClassId = targetClassId;
            student.SectionId = targetSectionId;
            student.GroupId = targetGroupId;
            student.Roll = targetRoll;

            var record = new StudentPromotionRecord
            {
                TenantId = tenantId,
                PublicId = Guid.NewGuid(),
                ClientRequestId = request.ClientRequestId,
                StudentId = student.Id,
                FromEnrollmentId = source.Id,
                ToEnrollment = target,
                FromAcademicYearId = source.AcademicYearId,
                ToAcademicYearId = targetYear.Id,
                FromClassId = source.ClassId,
                ToClassId = targetClass.Id,
                FromSectionId = source.SectionId,
                ToSectionId = targetSection.Id,
                FromGroupId = source.GroupId,
                ToGroupId = targetGroup?.Id,
                FromRoll = source.Roll,
                ToRoll = targetRoll,
                Decision = request.Decision,
                ProcessedAt = now,
                ProcessedByUserId = _currentUser.UserId,
                Note = note
            };
            await _records.AddAsync(record);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new ApiResponse<StudentPromotionResultDto>
            {
                Success = true,
                StatusCode = 201,
                Message = request.Decision == StudentProgressionDecision.Promoted
                    ? "Student promoted."
                    : "Student repeat placement recorded.",
                Data = Map(record, student, target, false)
            };
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent promotion for student {Reference} in tenant {TenantId}",
                studentReference, tenantId);
            return Error("The student or enrollment changed. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting promotion for student {Reference} in tenant {TenantId}",
                studentReference, tenantId);
            return Error("The promotion conflicts with an existing enrollment or request.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Promotion failed for student {Reference} in tenant {TenantId}",
                studentReference, tenantId);
            return Error("Student promotion failed.", 500);
        }
    }

    public async Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync(
        Guid studentReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<StudentPromotionHistoryDto>>();
        if (studentReference == Guid.Empty)
            return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.ErrorResponse(
                "Student reference is invalid.");

        try
        {
            var tenantId = _currentUser.TenantId;
            var student = await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == studentReference,
                    cancellationToken);
            if (student == null)
                return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.ErrorResponse(
                    "Student not found.", 404);

            IReadOnlyList<StudentPromotionHistoryDto> history = await _records.GetQueryable()
                .AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.StudentId == student.Id)
                .OrderByDescending(x => x.ProcessedAt)
                .Select(x => new StudentPromotionHistoryDto
                {
                    Reference = x.PublicId,
                    Decision = x.Decision,
                    FromAcademicYearId = x.FromAcademicYearId,
                    ToAcademicYearId = x.ToAcademicYearId,
                    FromClassId = x.FromClassId,
                    ToClassId = x.ToClassId,
                    FromSectionId = x.FromSectionId,
                    ToSectionId = x.ToSectionId,
                    FromGroupId = x.FromGroupId,
                    ToGroupId = x.ToGroupId,
                    FromRoll = x.FromRoll,
                    ToRoll = x.ToRoll,
                    ProcessedAt = x.ProcessedAt,
                    Note = x.Note
                })
                .ToListAsync(cancellationToken);
            return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.SuccessResponse(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Promotion history failed for student {Reference} in tenant {TenantId}",
                studentReference, _currentUser.TenantId);
            return ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>.ErrorResponse(
                "Promotion history could not be loaded.", 500);
        }
    }

    private async Task<ApiResponse<StudentPromotionResultDto>> ExistingResultAsync(
        StudentPromotionRecord record,
        Student student,
        CancellationToken cancellationToken)
    {
        var target = await _enrollments.FirstOrDefaultAsync(x =>
            x.TenantId == _currentUser.TenantId && x.Id == record.ToEnrollmentId);
        if (target == null) return Error("Existing promotion record is incomplete.", 409);
        return ApiResponse<StudentPromotionResultDto>.SuccessResponse(
            Map(record, student, target, true),
            "Student progression was already processed.");
    }

    private bool CanManage() =>
        _currentUser.IsAuthenticated
        && _currentUser.TenantId > 0
        && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("Principal"));

    private static bool IsValid(PromoteStudentRequestDto? request) =>
        request != null
        && request.ClientRequestId != Guid.Empty
        && request.SourceEnrollmentId > 0
        && request.TargetAcademicYearId > 0
        && request.TargetClassId > 0
        && request.TargetSectionId > 0
        && (!request.TargetGroupId.HasValue || request.TargetGroupId.Value > 0)
        && !string.IsNullOrWhiteSpace(request.TargetRoll)
        && request.TargetRoll.Trim().Length <= 50
        && (request.Note == null || request.Note.Trim().Length <= 500)
        && Enum.IsDefined(request.Decision);

    private static bool MatchesCurrentPlacement(Student student, Enrollment enrollment) =>
        student.AcademicYearId == enrollment.AcademicYearId
        && student.ClassId == enrollment.ClassId
        && student.SectionId == enrollment.SectionId
        && student.GroupId == enrollment.GroupId
        && string.Equals(student.Roll, enrollment.Roll, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesRequest(
        StudentPromotionRecord record,
        PromoteStudentRequestDto request,
        string targetRoll) =>
        record.ToAcademicYearId == request.TargetAcademicYearId
        && record.ToClassId == request.TargetClassId
        && record.ToSectionId == request.TargetSectionId
        && record.ToGroupId == request.TargetGroupId
        && record.Decision == request.Decision
        && string.Equals(record.ToRoll, targetRoll, StringComparison.OrdinalIgnoreCase);

    private static StudentPromotionResultDto Map(
        StudentPromotionRecord record,
        Student student,
        Enrollment target,
        bool alreadyProcessed) => new()
    {
        Reference = record.PublicId,
        StudentReference = student.PublicId,
        FromEnrollmentId = record.FromEnrollmentId,
        ToEnrollmentId = target.Id,
        Decision = record.Decision,
        AcademicYearId = record.ToAcademicYearId,
        ClassId = record.ToClassId,
        SectionId = record.ToSectionId,
        GroupId = record.ToGroupId,
        Roll = record.ToRoll,
        ProcessedAt = record.ProcessedAt,
        StudentRowVersion = Convert.ToBase64String(student.RowVersion),
        AlreadyProcessed = alreadyProcessed
    };

    private static bool TryVersion(string? value, out byte[] version)
    {
        try
        {
            version = Convert.FromBase64String(value ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            version = [];
            return false;
        }
    }

    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(actual, expected);

    private static bool TryLegacyId(long value, out int result)
    {
        result = 0;
        return value > 0 && value <= int.MaxValue && (result = (int)value) > 0;
    }

    private static bool TryNullableLegacyId(long? value, out int? result)
    {
        result = null;
        if (!value.HasValue) return true;
        if (!TryLegacyId(value.Value, out var parsed)) return false;
        result = parsed;
        return true;
    }

    private static ApiResponse<T> Denied<T>() =>
        ApiResponse<T>.ErrorResponse("Student promotion access is required.", 403);

    private static ApiResponse<StudentPromotionResultDto> Error(
        string message,
        int statusCode = 400) =>
        ApiResponse<StudentPromotionResultDto>.ErrorResponse(message, statusCode);
}
