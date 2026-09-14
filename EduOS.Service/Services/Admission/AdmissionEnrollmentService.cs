using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionEnrollmentService : IAdmissionEnrollmentService
{
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<Enrollment> _enrollments;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<StudentPersonLink> _personLinks;
    private readonly IGenericRepository<Section> _sections;
    private readonly IGenericRepository<Group> _groups;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionEnrollmentService> _logger;

    public AdmissionEnrollmentService(IGenericRepository<AdmissionApplicant> applications, IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians, IGenericRepository<Enrollment> enrollments, IGenericRepository<Person> persons,
        IGenericRepository<StudentPersonLink> personLinks, IGenericRepository<Section> sections, IGenericRepository<Group> groups,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock, ILogger<AdmissionEnrollmentService> logger)
    {
        _applications = applications;
        _students = students;
        _guardians = guardians;
        _enrollments = enrollments;
        _persons = persons;
        _personLinks = personLinks;
        _sections = sections;
        _groups = groups;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AdmissionEnrollmentOptionsDto>> GetOptionsAsync(Guid applicationReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionEnrollmentOptionsDto>();
        var application = await _applications.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == applicationReference, cancellationToken);
        if (application == null) return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Application not found.", 404);
        if (application.Status is not (AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Admitted))
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Only an approved application can be admitted.", 409);
        if (!TryLegacyId(application.AcademicUnitId, out var academicUnitId))
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Academic unit cannot be used for enrollment.", 409);

        var sections = await _sections.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.ClassId == academicUnitId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name, ParentId = application.AcademicUnitId })
            .ToListAsync(cancellationToken);
        var groups = await _groups.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);
        return ApiResponse<AdmissionEnrollmentOptionsDto>.SuccessResponse(new AdmissionEnrollmentOptionsDto { Sections = sections, Groups = groups });
    }

    public async Task<ApiResponse<AdmittedStudentDto>> AdmitAsync(Guid applicationReference, AdmitAdmissionApplicationDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmittedStudentDto>();
        if (applicationReference == Guid.Empty || request == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Admission request is invalid.");
        var roll = request.Roll?.Trim();
        if (string.IsNullOrWhiteSpace(roll) || roll.Length > 50) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Roll is required.");
        if (!TryVersion(request.RowVersion, out var expectedVersion)) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Row version is invalid.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                try
                {
            var existing = await _students.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AdmissionApplicationId != null
                                          && x.AdmissionApplication!.PublicId == applicationReference, cancellationToken);
            if (existing != null)
            {
                if (existing.Id > int.MaxValue) throw new InvalidOperationException("Student reference exceeds the legacy enrollment range.");
                var existingStudentId = checked((int)existing.Id);
                var existingEnrollment = await _enrollments.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenantId && x.StudentId == existingStudentId)
                    .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
                return ApiResponse<AdmittedStudentDto>.SuccessResponse(Map(existing, existingEnrollment), "Applicant is already admitted.");
            }

            var application = await _applications.GetQueryable()
                .Include(x => x.AcademicYear).Include(x => x.AcademicTerm).Include(x => x.Campus).Include(x => x.AcademicUnit)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == applicationReference, cancellationToken);
            if (application == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Application not found.", 404);
            if (application.Status != AdmissionApplicationStatus.Approved)
                return ApiResponse<AdmittedStudentDto>.ErrorResponse("Only an approved application can be admitted.", 409);
            if (!VersionsMatch(application.RowVersion, expectedVersion))
                return ApiResponse<AdmittedStudentDto>.ErrorResponse("The application was changed by another user. Reload and try again.", 409);
            if (!TryLegacyId(application.AcademicYearId, out var yearId) || !TryLegacyId(application.AcademicUnitId, out var unitId)
                || !TryLegacyId(request.SectionId, out var sectionId) || !TryNullableLegacyId(request.GroupId, out var groupId))
                return ApiResponse<AdmittedStudentDto>.ErrorResponse("Enrollment reference is invalid.", 409);

            var section = await _sections.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.SectionId
                                                                                  && x.ClassId == unitId && x.IsActive, cancellationToken);
            if (section == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Section does not belong to the selected academic unit.", 409);
            Group? group = null;
            if (request.GroupId.HasValue)
            {
                group = await _groups.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == request.GroupId.Value && x.IsActive, cancellationToken);
                if (group == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Group is unavailable.", 409);
            }
            if (await _enrollments.AnyAsync(x => x.TenantId == tenantId && x.AcademicYearId == yearId && x.ClassId == unitId
                                                 && x.SectionId == sectionId && x.Roll == roll && x.IsActive))
                return ApiResponse<AdmittedStudentDto>.ErrorResponse("Roll is already assigned in this section.", 409);

            await _unitOfWork.BeginTransactionAsync();
            var now = _clock.GetUtcNow().UtcDateTime;
            var student = BuildStudent(application, section, group, yearId, unitId, sectionId, groupId, roll, now);
            var person = new Person { PublicId = Guid.NewGuid(), FullName = student.FullName, FullNameBangla = student.FullNameBangla,
                DateOfBirth = student.DOB.Date, Gender = student.Gender };
            await _students.AddAsync(student);
            await _persons.AddAsync(person);
            await _personLinks.AddAsync(new StudentPersonLink { TenantId = tenantId, Student = student, Person = person,
                Status = StudentPersonLinkStatus.Active, LinkedAt = now, LinkedByUserId = _currentUser.UserId });

            if (!string.IsNullOrWhiteSpace(application.GuardianName) && !string.IsNullOrWhiteSpace(application.GuardianMobile))
            {
                await _guardians.AddAsync(new Guardian { TenantId = tenantId, PublicId = Guid.NewGuid(), Student = student,
                    Name = application.GuardianName, Relation = application.GuardianRelation ?? "Guardian", Phone = application.GuardianMobile,
                    Address = application.PermanentAddress ?? application.PresentAddress, IsPrimary = true });
            }
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            if (student.Id > int.MaxValue) throw new InvalidOperationException("Student reference exceeds the legacy enrollment range.");

            var enrollment = new Enrollment { TenantId = tenantId, StudentId = checked((int)student.Id), Student = student,
                AcademicYearId = yearId, AcademicYear = application.AcademicYear, ClassId = unitId, Class = application.AcademicUnit,
                SectionId = sectionId, Section = section, GroupId = groupId, Group = group, CampusId = application.CampusId,
                Campus = application.Campus, AcademicTermId = application.AcademicTermId, AcademicTerm = application.AcademicTerm,
                Roll = roll, EnrollmentDate = now, IsActive = true };
            await _enrollments.AddAsync(enrollment);
            application.Status = AdmissionApplicationStatus.Admitted;
            application.UpdatedAt = now;
            application.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _unitOfWork.CommitTransactionAsync();

                    return new ApiResponse<AdmittedStudentDto> { Success = true, StatusCode = 201, Message = "Applicant admitted.", Data = Map(student, enrollment) };
                }
                catch
                {
                    await SafeRollbackAsync();
                    throw;
                }
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync();
            _logger.LogWarning(ex, "Concurrent admission conversion {Reference} for tenant {TenantId}", applicationReference, tenantId);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("The application was changed by another user. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            await SafeRollbackAsync();
            _logger.LogWarning(ex, "Conflicting admission conversion {Reference} for tenant {TenantId}", applicationReference, tenantId);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("The applicant or roll was already admitted. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync();
            _logger.LogError(ex, "Admission conversion failed for {Reference} in tenant {TenantId}", applicationReference, tenantId);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant could not be admitted.", 500);
        }
    }

    private Student BuildStudent(AdmissionApplicant application, Section section, Group? group, int yearId, int unitId,
        int sectionId, int? groupId, string roll, DateTime now)
    {
        var relation = application.GuardianRelation?.Trim();
        return new Student
        {
            TenantId = _currentUser.TenantId, PublicId = Guid.NewGuid(), AdmissionApplicationId = application.Id,
            AdmissionApplication = application, StudentCode = $"STU-{application.PublicId:N}".ToUpperInvariant(), Roll = roll,
            FullName = application.ApplicantName, FullNameBangla = application.ApplicantNameBangla,
            FatherName = IsRelation(relation, "father", "পিতা") ? application.GuardianName ?? string.Empty : string.Empty,
            MotherName = IsRelation(relation, "mother", "মাতা") ? application.GuardianName ?? string.Empty : string.Empty,
            DOB = application.DateOfBirth.Date, Gender = application.Gender.ToString(), Phone = application.PrimaryMobile,
            Email = application.Email, Address = application.PermanentAddress ?? application.PresentAddress,
            ClassId = unitId, Class = application.AcademicUnit, SectionId = sectionId, Section = section, GroupId = groupId, Group = group,
            AcademicYearId = yearId, AcademicYear = application.AcademicYear, AdmissionDate = now,
            PreferredLanguage = application.PreferredLanguage, Status = "Active", IsActive = true
        };
    }

    private static AdmittedStudentDto Map(Student student, Enrollment? enrollment) => new()
    {
        StudentReference = student.PublicId, StudentCode = student.StudentCode, Roll = student.Roll, StudentId = student.Id,
        EnrollmentId = enrollment?.Id ?? 0, ApplicationStatus = AdmissionApplicationStatus.Admitted
    };

    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0
                                && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Admission officer access is required.", 403);
    private static bool TryLegacyId(long value, out int result) { result = 0; return value > 0 && value <= int.MaxValue && (result = (int)value) > 0; }
    private static bool TryNullableLegacyId(long? value, out int? result) { result = null; if (!value.HasValue) return true; if (!TryLegacyId(value.Value, out var parsed)) return false; result = parsed; return true; }
    private static bool TryVersion(string? value, out byte[] version) { try { version = Convert.FromBase64String(value ?? string.Empty); return true; } catch (FormatException) { version = []; return false; } }
    private static bool VersionsMatch(byte[] actual, byte[] expected) => actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
    private static bool IsRelation(string? value, string english, string bangla) => string.Equals(value, english, StringComparison.OrdinalIgnoreCase) || string.Equals(value, bangla, StringComparison.Ordinal);
    private async Task SafeRollbackAsync() { try { await _unitOfWork.RollbackTransactionAsync(); } catch (Exception ex) { _logger.LogError(ex, "Admission rollback failed."); } }
}
