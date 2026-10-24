using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.Academic;

/// <summary>Tenant-scoped read model; all mutations reuse AcademicEnrollmentService.</summary>
public sealed class StudentOptionalSubjectQueryService : IStudentOptionalSubjectQueryService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<SubjectOffering> _offerings;
    private readonly IGenericRepository<CurriculumSubject> _curriculumSubjects;
    private readonly IGenericRepository<Subject> _subjects;
    private readonly IGenericRepository<StudentSubjectRegistration> _registrations;
    private readonly IGenericRepository<SubjectPrerequisite> _prerequisites;
    private readonly ICurrentUserService _user;

    public StudentOptionalSubjectQueryService(IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<SubjectOffering> offerings,
        IGenericRepository<CurriculumSubject> curriculumSubjects,
        IGenericRepository<Subject> subjects,
        IGenericRepository<StudentSubjectRegistration> registrations,
        IGenericRepository<SubjectPrerequisite> prerequisites, ICurrentUserService user)
    {
        _students = students; _enrollments = enrollments; _offerings = offerings;
        _curriculumSubjects = curriculumSubjects; _subjects = subjects;
        _registrations = registrations; _prerequisites = prerequisites; _user = user;
    }

    public async Task<ApiResponse<StudentOptionalSubjectWorkspaceDto>> GetAsync(
        Guid studentReference, CancellationToken ct = default)
    {
        if (!_user.IsAuthenticated || !_user.IsTenantAdmin || _user.TenantId <= 0)
            return ApiResponse<StudentOptionalSubjectWorkspaceDto>.ErrorResponse("Permission denied.", 403);
        if (studentReference == Guid.Empty)
            return ApiResponse<StudentOptionalSubjectWorkspaceDto>.ErrorResponse("Student not found.", 404);
        var tenant = _user.TenantId;
        var student = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == studentReference &&
            x.StatusCode == "Active" && !x.IsDeleted, ct);
        if (student == null)
            return ApiResponse<StudentOptionalSubjectWorkspaceDto>.ErrorResponse("Student not found.", 404);
        var enrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.StudentId == student.Id && x.IsCurrent &&
            x.State == EnrollmentState.Active && !x.IsDeleted, ct);
        if (enrollment == null)
            return ApiResponse<StudentOptionalSubjectWorkspaceDto>.ErrorResponse("Current enrollment not found.", 404);

        var options = await (from offering in _offerings.GetQueryable().AsNoTracking()
            join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals item.Id
            join subject in _subjects.GetQueryable().AsNoTracking()
                on item.SubjectId equals subject.Id
            where offering.TenantId == tenant && item.TenantId == tenant &&
                subject.TenantId == tenant && !offering.IsDeleted && !item.IsDeleted &&
                !subject.IsDeleted && offering.IsActive && item.IsActive && item.IsOptional &&
                offering.AcademicBatchId == enrollment.AcademicBatchId &&
                offering.AcademicYearId == enrollment.AcademicYearId &&
                item.AcademicCurriculumId == enrollment.AcademicCurriculumId &&
                item.AcademicLevelId == enrollment.AcademicLevelId
            orderby subject.Name, offering.Code
            select new { item.SubjectId, Offering = new OptionalSubjectOfferingDto
            {
                Reference = offering.PublicId, Code = subject.Code,
                SubjectName = subject.Name, Capacity = offering.Capacity
            } }).Take(200).ToListAsync(ct);
        var helper = new SubjectPrerequisiteEligibility(_prerequisites, _enrollments, _registrations,
            _offerings, _curriculumSubjects, _subjects);
        var missing = await helper.MissingAsync(tenant, student.Id, enrollment.CampusId,
            enrollment.AcademicCurriculumId, options.Select(x => x.SubjectId).ToArray(),
            DateOnly.FromDateTime(DateTime.UtcNow), ct);
        foreach (var option in options)
        {
            option.Offering.MissingPrerequisites = missing.GetValueOrDefault(option.SubjectId) ?? Array.Empty<string>();
            option.Offering.IsEligible = option.Offering.MissingPrerequisites.Count == 0;
        }

        var registrations = await (from registration in _registrations.GetQueryable().AsNoTracking()
            join offering in _offerings.GetQueryable().AsNoTracking()
                on registration.SubjectOfferingId equals offering.Id
            join item in _curriculumSubjects.GetQueryable().AsNoTracking()
                on offering.CurriculumSubjectId equals item.Id
            join subject in _subjects.GetQueryable().AsNoTracking()
                on item.SubjectId equals subject.Id
            where registration.TenantId == tenant && offering.TenantId == tenant &&
                item.TenantId == tenant && subject.TenantId == tenant &&
                !registration.IsDeleted && registration.StudentEnrollmentId == enrollment.Id
            orderby subject.Name
            select new StudentSubjectRegistrationDto
            {
                Id = registration.Id, StudentEnrollmentId = registration.StudentEnrollmentId,
                SubjectOfferingId = registration.SubjectOfferingId, SubjectCode = subject.Code,
                SubjectName = subject.Name, State = registration.State,
                RegisteredAt = registration.RegisteredAt, ApprovedByUserId = registration.ApprovedByUserId,
                ApprovedAt = registration.ApprovedAt, Remarks = registration.Remarks,
                RowVersion = Convert.ToBase64String(registration.RowVersion)
            }).Take(300).ToListAsync(ct);

        return ApiResponse<StudentOptionalSubjectWorkspaceDto>.SuccessResponse(new StudentOptionalSubjectWorkspaceDto
        {
            StudentReference = student.PublicId, EnrollmentReference = enrollment.PublicId,
            StudentName = student.FullName, StudentCode = student.StudentCode, RollNo = enrollment.RollNo,
            Offerings = options.Select(x => x.Offering).ToList(), Registrations = registrations
        });
    }
}
