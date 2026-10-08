using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionEnrollmentService : IAdmissionEnrollmentService
{
    private readonly IGenericRepository<AdmissionApplicant> _applicants;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionDecision> _decisions;
    private readonly IGenericRepository<AdmissionApplicantGuardian> _applicantGuardians;
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<StudentGuardian> _studentGuardians;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<StudentPersonLink> _personLinks;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AcademicTrack> _tracks;
    private readonly IGenericRepository<AcademicCurriculum> _curricula;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionEnrollmentService> _logger;

    public AdmissionEnrollmentService(IGenericRepository<AdmissionApplicant> applicants,
        IGenericRepository<AdmissionIntakeForm> forms, IGenericRepository<AdmissionDecision> decisions,
        IGenericRepository<AdmissionApplicantGuardian> applicantGuardians,
        IGenericRepository<Student> students, IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<Guardian> guardians, IGenericRepository<StudentGuardian> studentGuardians,
        IGenericRepository<Person> persons, IGenericRepository<StudentPersonLink> personLinks,
        IGenericRepository<AcademicBatch> batches, IGenericRepository<AcademicTrack> tracks,
        IGenericRepository<AcademicCurriculum> curricula, IGenericRepository<AcademicYear> years,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<AdmissionEnrollmentService> logger)
    {
        _applicants = applicants; _forms = forms; _decisions = decisions;
        _applicantGuardians = applicantGuardians; _students = students;
        _enrollments = enrollments; _guardians = guardians; _studentGuardians = studentGuardians;
        _persons = persons; _personLinks = personLinks; _batches = batches;
        _tracks = tracks; _curricula = curricula; _years = years;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<AdmissionEnrollmentOptionsDto>> GetOptionsAsync(Guid applicationReference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionEnrollmentOptionsDto>();
        if (applicationReference == Guid.Empty)
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant reference is required.");
        var tenant = _user.TenantId;
        var application = await _applicants.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant && x.PublicId == applicationReference, cancellationToken);
        if (application == null) return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant not found.", 404);
        if (application.State is not (AdmissionApplicantState.Qualified or AdmissionApplicantState.Admitted))
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Applicant must qualify before enrollment.", 409);
        var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == application.AdmissionIntakeFormId, cancellationToken);
        if (form == null) return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("Intake form not found.", 409);
        var accepted = await _decisions.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.AdmissionApplicantId == application.Id && x.State == AdmissionDecisionState.Accepted)
            .OrderByDescending(x => x.AcceptedAt).ThenByDescending(x => x.Id)
            .Select(x => x.OfferedAcademicBatchId).FirstOrDefaultAsync(cancellationToken);
        if (!accepted.HasValue && application.State != AdmissionApplicantState.Admitted)
            return ApiResponse<AdmissionEnrollmentOptionsDto>.ErrorResponse("An accepted admission offer is required.", 409);
        var sectionsQuery = _batches.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.IsActive && x.CampusId == form.CampusId && x.AcademicYearId == form.AcademicYearId &&
            x.AcademicProgramId == form.AcademicProgramId && x.AcademicLevelId == form.AcademicLevelId);
        if (form.AcademicTermId.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.AcademicTermId == form.AcademicTermId);
        if (form.AcademicTrackId.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.AcademicTrackId == form.AcademicTrackId);
        if (accepted.HasValue)
            sectionsQuery = sectionsQuery.Where(x => x.Id == accepted.Value);
        var sections = await sectionsQuery.OrderBy(x => x.Name).Select(x =>
            new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name, ParentId = x.AcademicLevelId })
            .ToListAsync(cancellationToken);
        var tracks = await _tracks.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive &&
            (x.AcademicProgramId == null || x.AcademicProgramId == form.AcademicProgramId))
            .OrderBy(x => x.Name).Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
            .ToListAsync(cancellationToken);
        return ApiResponse<AdmissionEnrollmentOptionsDto>.SuccessResponse(new AdmissionEnrollmentOptionsDto
        {
            Sections = sections, Groups = tracks
        });
    }

    public async Task<ApiResponse<AdmittedStudentDto>> AdmitAsync(Guid applicationReference,
        AdmitAdmissionApplicationDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmittedStudentDto>();
        if (applicationReference == Guid.Empty || request == null || request.SectionId <= 0 ||
            request.GroupId is <= 0 || string.IsNullOrWhiteSpace(request.Roll) || request.Roll.Trim().Length > 50 ||
            !TryVersion(request.RowVersion, out var expected))
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Valid applicant, batch, roll and row version are required.");
        var tenant = _user.TenantId;
        var roll = request.Roll.Trim();
        try
        {
            var strategy = _uow.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var started = false;
                try
                {
                    await _uow.BeginTransactionAsync();
                    started = true;
                    var application = await _applicants.GetQueryable().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.PublicId == applicationReference, cancellationToken);
                    if (application == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant not found.", 404);
                    if (application.ConvertedStudentId.HasValue && application.ConvertedEnrollmentId.HasValue)
                    {
                        var existingStudent = await _students.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.TenantId == tenant && x.Id == application.ConvertedStudentId.Value, cancellationToken);
                        var existingEnrollment = await _enrollments.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.TenantId == tenant && x.Id == application.ConvertedEnrollmentId.Value, cancellationToken);
                        if (existingStudent == null || existingEnrollment == null ||
                            existingStudent.AdmissionApplicantId != application.Id ||
                            existingEnrollment.StudentId != existingStudent.Id)
                            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant conversion has inconsistent references.", 409);
                        return ApiResponse<AdmittedStudentDto>.SuccessResponse(Map(existingStudent, existingEnrollment),
                            "Applicant was already admitted.");
                    }
                    if (application.State != AdmissionApplicantState.Qualified)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Only a qualified applicant can be admitted.", 409);
                    if (!VersionsMatch(application.RowVersion, expected))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant changed. Reload and retry.", 409);
                    if (await _students.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id, cancellationToken))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant has an existing student record.", 409);

                    var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == application.AdmissionIntakeFormId, cancellationToken);
                    if (form == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Intake form not found.", 409);
                    var decision = await _decisions.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id)
                        .OrderByDescending(x => x.Id).FirstOrDefaultAsync(cancellationToken);
                    if (decision == null || decision.State != AdmissionDecisionState.Accepted ||
                        decision.OfferedAcademicBatchId != request.SectionId ||
                        (decision.ExpiresAt.HasValue && decision.ExpiresAt.Value < _clock.GetUtcNow().UtcDateTime))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("An accepted offer for the selected batch is required.", 409);

                    var batch = await _batches.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == request.SectionId && x.IsActive &&
                        x.CampusId == form.CampusId && x.AcademicYearId == form.AcademicYearId &&
                        x.AcademicProgramId == form.AcademicProgramId && x.AcademicLevelId == form.AcademicLevelId &&
                        (!form.AcademicTermId.HasValue || x.AcademicTermId == form.AcademicTermId) &&
                        (!form.AcademicTrackId.HasValue || x.AcademicTrackId == form.AcademicTrackId),
                        cancellationToken);
                    if (batch == null) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Batch is not eligible for this intake.", 409);
                    if (request.GroupId.HasValue && request.GroupId != batch.AcademicTrackId)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Selected academic track does not match the batch.", 409);
                    var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
                    var yearActive = await _years.GetQueryable().AsNoTracking().AnyAsync(x =>
                        x.TenantId == tenant && x.Id == batch.AcademicYearId && x.IsActive &&
                        today >= x.StartDate && today <= x.EndDate, cancellationToken);
                    if (!yearActive) return ApiResponse<AdmittedStudentDto>.ErrorResponse("Academic year is not active for today's admission date.", 409);
                    var curricula = await _curricula.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AcademicProgramId == batch.AcademicProgramId &&
                        x.IsCurrent && x.IsActive && x.AcademicTrackId == batch.AcademicTrackId &&
                        x.MediumId == batch.MediumId && x.EffectiveFrom <= today &&
                        (!x.EffectiveTo.HasValue || x.EffectiveTo >= today))
                        .Take(2).Select(x => x.Id).ToArrayAsync(cancellationToken);
                    if (curricula.Length != 1)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Exactly one active curriculum must match the batch.", 409);
                    if (batch.Capacity > 0 && await _enrollments.GetQueryable().AsNoTracking().CountAsync(x =>
                        x.TenantId == tenant && x.AcademicBatchId == batch.Id &&
                        x.IsCurrent && x.IsActive && x.State == EnrollmentState.Active, cancellationToken) >= batch.Capacity)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("The batch is at capacity.", 409);
                    if (await _enrollments.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                        x.AcademicBatchId == batch.Id && x.RollNo == roll && x.IsCurrent && x.IsActive, cancellationToken))
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Roll is already assigned in this batch.", 409);

                    var now = _clock.GetUtcNow().UtcDateTime;
                    var person = application.PersonId.HasValue
                        ? await _persons.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                            x.Id == application.PersonId.Value, cancellationToken)
                        : null;
                    if (application.PersonId.HasValue && person == null)
                        return ApiResponse<AdmittedStudentDto>.ErrorResponse("Linked person record is missing.", 409);
                    if (person == null)
                    {
                        person = new Person
                        {
                            FullName = application.FullName, FullNameBangla = application.FullNameBangla,
                            DateOfBirth = application.DateOfBirth, Gender = application.Gender,
                            Phone = application.Phone, Email = application.Email, CreatedAt = now
                        };
                        await _persons.AddAsync(person);
                        await _uow.SaveChangesAsync(cancellationToken);
                        application.PersonId = person.Id;
                    }
                    var student = new Student
                    {
                        TenantId = tenant, PublicId = Guid.NewGuid(), PersonId = person.Id,
                        AdmissionApplicantId = application.Id, StudentCode = "STU-" + application.PublicId.ToString("N").ToUpperInvariant(),
                        FullName = application.FullName, FullNameBangla = application.FullNameBangla,
                        DateOfBirth = application.DateOfBirth, Gender = application.Gender,
                        Phone = application.Phone, Email = application.Email, Address = application.Address,
                        AdmissionDate = today, PreferredLanguage = person.PreferredLanguage,
                        StatusCode = "Active", IsActive = true, CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _students.AddAsync(student);
                    await _uow.SaveChangesAsync(cancellationToken);
                    await _personLinks.AddAsync(new StudentPersonLink
                    {
                        TenantId = tenant, StudentId = student.Id, PersonId = person.Id,
                        IsPrimary = true, LinkedAt = now, LinkedByUserId = _user.UserId,
                        CreatedAt = now, CreatedBy = _user.UserId
                    });
                    var guardianInputs = await _applicantGuardians.GetQueryable().AsNoTracking().Where(x =>
                        x.TenantId == tenant && x.AdmissionApplicantId == application.Id).OrderByDescending(x => x.IsPrimary)
                        .ToListAsync(cancellationToken);
                    foreach (var input in guardianInputs)
                    {
                        var guardianPerson = new Person
                        {
                            FullName = input.FullName, Phone = input.Phone, Email = input.Email,
                            CreatedAt = now
                        };
                        await _persons.AddAsync(guardianPerson);
                        await _uow.SaveChangesAsync(cancellationToken);
                        var guardian = new Guardian
                        {
                            TenantId = tenant, PersonId = guardianPerson.Id, FullName = input.FullName,
                            Phone = input.Phone, Email = input.Email, Occupation = input.Occupation,
                            CreatedAt = now, CreatedBy = _user.UserId
                        };
                        await _guardians.AddAsync(guardian);
                        await _uow.SaveChangesAsync(cancellationToken);
                        await _studentGuardians.AddAsync(new StudentGuardian
                        {
                            TenantId = tenant, StudentId = student.Id, GuardianId = guardian.Id,
                            RelationCode = input.RelationCode, IsPrimary = input.IsPrimary,
                            CreatedAt = now, CreatedBy = _user.UserId
                        });
                    }

                    var enrollment = new StudentEnrollment
                    {
                        TenantId = tenant, PublicId = Guid.NewGuid(), ClientRequestId = Guid.NewGuid(),
                        StudentId = student.Id, CampusId = batch.CampusId,
                        AcademicYearId = batch.AcademicYearId, AcademicTermId = batch.AcademicTermId,
                        AcademicProgramId = batch.AcademicProgramId, AcademicLevelId = batch.AcademicLevelId,
                        AcademicBatchId = batch.Id, AcademicCurriculumId = curricula[0],
                        AcademicTrackId = batch.AcademicTrackId, MediumId = batch.MediumId, ShiftId = batch.ShiftId,
                        RollNo = roll, EnrollmentDate = today, State = EnrollmentState.Active,
                        IsCurrent = true, IsActive = true, CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _enrollments.AddAsync(enrollment);
                    await _uow.SaveChangesAsync(cancellationToken);
                    application.State = AdmissionApplicantState.Admitted;
                    application.ConvertedStudentId = student.Id;
                    application.ConvertedEnrollmentId = enrollment.Id;
                    application.ConvertedAt = now;
                    application.UpdatedAt = now;
                    application.UpdatedBy = _user.UserId;
                    await _uow.SaveChangesAsync(cancellationToken);
                    var result = Map(student, enrollment);
                    await _uow.CommitTransactionAsync();
                    started = false;
                    return new ApiResponse<AdmittedStudentDto>
                    {
                        Success = true, StatusCode = 201, Message = "Applicant admitted.", Data = result
                    };
                }
                finally
                {
                    if (started) await _uow.RollbackTransactionAsync();
                }
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Admission conversion version conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission conversion integrity conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Conversion conflicts with an existing student or roll.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission conversion failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmittedStudentDto>.ErrorResponse("Applicant could not be admitted.", 500);
        }
    }

    private static AdmittedStudentDto Map(Student student, StudentEnrollment enrollment) => new()
    {
        StudentReference = student.PublicId, StudentCode = student.StudentCode,
        StudentId = student.Id, EnrollmentId = enrollment.Id, Roll = enrollment.RollNo,
        ApplicationStatus = AdmissionApplicationStatus.Admitted
    };

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Admission officer access is required.", 403);
    private static bool TryVersion(string? supplied, out byte[] version)
    {
        version = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(supplied)) return false;
        try { version = Convert.FromBase64String(supplied); return version.Length > 0; }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] actual, byte[] expected) =>
        actual.Length == expected.Length && CryptographicOperations.FixedTimeEquals(actual, expected);
}
