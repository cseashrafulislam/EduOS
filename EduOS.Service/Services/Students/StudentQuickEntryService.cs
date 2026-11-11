using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

/// <summary>
/// One short atomic path for onboarding an already-running student. New admissions still
/// follow their separate approval workflow; enrollment authority remains AcademicEnrollmentService.
/// </summary>
public sealed class StudentQuickEntryService(
    EduOSDbContext db, IUnitOfWork unitOfWork, IAcademicEnrollmentService academicEnrollments,
    ICurrentUserService user, TimeProvider clock, ILogger<StudentQuickEntryService> logger)
{
    private sealed class RejectEntry(string message, int status = 409) : Exception(message)
    {
        public int Status { get; } = status;
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<ApiResponse<RunningStudentCreatedDto>> CreateAsync(
        CreateRunningStudentRequestDto request, CancellationToken ct = default)
    {
        if (!user.IsAuthenticated || !user.IsTenantAdmin || user.TenantId <= 0 ||
            db.CurrentTenantId != user.TenantId)
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse("Student creation is not authorized.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.AcademicBatchId <= 0 ||
            request.AcademicCurriculumId <= 0 || request.AdmissionDate == default ||
            request.EnrollmentDate == default || string.IsNullOrWhiteSpace(request.FullName) ||
            request.FullName.Trim().Length is < 2 or > 200 ||
            string.IsNullOrWhiteSpace(request.RollNo) || request.RollNo.Trim().Length > 50 ||
            request.DateOfBirth > DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime) ||
            request.Guardian != null && (string.IsNullOrWhiteSpace(request.Guardian.FullName) ||
                string.IsNullOrWhiteSpace(request.Guardian.RelationCode)))
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse("Student, academic section, dates, roll and complete optional guardian details are required.");

        var tenant = user.TenantId;
        var name = request.FullName.Trim();
        var roll = request.RollNo.Trim();
        var phone = Optional(request.Phone);
        var guardianName = Optional(request.Guardian?.FullName);
        try
        {
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                // Idempotency is owned by the canonical enrollment's tenant-scoped ClientRequestId.
                // Replays must not create a second Person/Student/Guardian graph.
                var replay = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, token);
                if (replay != null)
                {
                    var existing = await db.Students.AsNoTracking().FirstOrDefaultAsync(x =>
                        x.TenantId == tenant && x.Id == replay.StudentId && !x.IsDeleted, token);
                    if (existing == null || existing.AdmissionApplicantId.HasValue ||
                        existing.FullName != name || existing.AdmissionDate != request.AdmissionDate ||
                        replay.AcademicBatchId != request.AcademicBatchId ||
                        replay.AcademicCurriculumId != request.AcademicCurriculumId ||
                        replay.RollNo != roll || replay.EnrollmentDate != request.EnrollmentDate ||
                        existing.Phone != phone || existing.DateOfBirth != request.DateOfBirth)
                        throw new RejectEntry("Request reference was used for different student data.");
                    var linkedGuardian = await (from link in db.StudentGuardians.AsNoTracking()
                        join guardian in db.Guardians.AsNoTracking() on link.GuardianId equals guardian.Id
                        where link.TenantId == tenant && link.StudentId == existing.Id && !link.IsDeleted &&
                              guardian.TenantId == tenant && !guardian.IsDeleted
                        select new { guardian.FullName, guardian.Phone, link.RelationCode }).FirstOrDefaultAsync(token);
                    if (guardianName == null && linkedGuardian != null ||
                        guardianName != null && (linkedGuardian == null ||
                            linkedGuardian.FullName != guardianName ||
                            linkedGuardian.Phone != Optional(request.Guardian?.Phone) ||
                            linkedGuardian.RelationCode != Optional(request.Guardian?.RelationCode)))
                        throw new RejectEntry("Request reference was used for different guardian data.");
                    return ApiResponse<RunningStudentCreatedDto>.SuccessResponse(new RunningStudentCreatedDto
                    {
                        StudentReference = existing.PublicId, StudentCode = existing.StudentCode,
                        FullName = existing.FullName, EnrollmentReference = replay.PublicId,
                        RollNo = replay.RollNo, GuardianLinked = linkedGuardian != null
                    }, "Student already saved.");
                }

                // Prevent accidental double creation when the existing student can be identified.
                // Do not equate people using names alone or silently reuse a guardian by phone.
                if (request.DateOfBirth.HasValue && phone != null &&
                    await db.Students.AsNoTracking().AnyAsync(x => x.TenantId == tenant && !x.IsDeleted &&
                        x.FullName == name && x.DateOfBirth == request.DateOfBirth && x.Phone == phone, token))
                    throw new RejectEntry("Possible existing student. Search Student List before saving.");

                var batch = await db.AcademicBatches.FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AcademicBatchId && x.IsActive && !x.IsDeleted, token);
                if (batch == null) throw new RejectEntry("Academic section is no longer available.");
                var now = clock.GetUtcNow().UtcDateTime;

                // Same batch write mutex/order as the canonical enrollment, admission and import paths.
                // Hold it while checking subscription seats and creating the entire graph.
                batch.UpdatedAt = now;
                batch.UpdatedBy = user.UserId;
                await db.SaveChangesAsync(token);
                if (!await SubscriptionActiveCapacityPolicy.CanAddStudentsAsync(db, tenant, 1, token))
                    throw new RejectEntry("Subscription active student capacity reached.");

                var person = new Person
                {
                    FullName = name, FullNameBangla = Optional(request.FullNameBangla),
                    DateOfBirth = request.DateOfBirth, Gender = Optional(request.Gender),
                    Phone = phone, PreferredLanguage = "bn-BD", CreatedAt = now
                };
                db.Persons.Add(person);
                await db.SaveChangesAsync(token);

                var reference = Guid.NewGuid();
                var student = new Student
                {
                    TenantId = tenant, PublicId = reference, PersonId = person.Id,
                    StudentCode = "STU-" + reference.ToString("N").ToUpperInvariant(),
                    FullName = name, FullNameBangla = person.FullNameBangla,
                    DateOfBirth = request.DateOfBirth, Gender = person.Gender, Phone = phone,
                    Address = Optional(request.Address), AdmissionDate = request.AdmissionDate,
                    PreferredLanguage = person.PreferredLanguage, StatusCode = "Active",
                    CreatedAt = now, CreatedBy = user.UserId
                };
                db.Students.Add(student);
                await db.SaveChangesAsync(token);
                db.StudentPersonLinks.Add(new StudentPersonLink
                {
                    TenantId = tenant, StudentId = student.Id, PersonId = person.Id,
                    IsPrimary = true, LinkedAt = now, LinkedByUserId = user.UserId,
                    CreatedAt = now, CreatedBy = user.UserId
                });
                await db.SaveChangesAsync(token);

                // Existing enrollment service validates section, effective curriculum,
                // offerings, roll uniqueness, capacity and access. A failed response MUST
                // throw to roll back the previously created student and person.
                var enrolled = await academicEnrollments.EnrollAsync(new CreateStudentEnrollmentRequestDto
                {
                    ClientRequestId = request.ClientRequestId, StudentReference = reference,
                    AcademicBatchId = request.AcademicBatchId,
                    AcademicCurriculumId = request.AcademicCurriculumId,
                    EnrollmentDate = request.EnrollmentDate, RollNo = roll
                }, token);
                if (!enrolled.Success || enrolled.Data == null)
                    throw new RejectEntry(enrolled.Message ?? "Enrollment was not saved.", enrolled.StatusCode);

                if (request.Guardian != null)
                {
                    var g = request.Guardian;
                    var guardianPerson = new Person
                    {
                        FullName = g.FullName.Trim(), Phone = Optional(g.Phone),
                        Email = Optional(g.Email), CreatedAt = now
                    };
                    db.Persons.Add(guardianPerson);
                    await db.SaveChangesAsync(token);
                    var guardian = new Guardian
                    {
                        TenantId = tenant, PersonId = guardianPerson.Id, FullName = guardianPerson.FullName,
                        Phone = guardianPerson.Phone, Email = guardianPerson.Email,
                        IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                    };
                    db.Guardians.Add(guardian);
                    await db.SaveChangesAsync(token);
                    db.StudentGuardians.Add(new StudentGuardian
                    {
                        TenantId = tenant, StudentId = student.Id, GuardianId = guardian.Id,
                        RelationCode = g.RelationCode.Trim(), IsPrimary = true,
                        CanReceiveSms = g.CanReceiveSms && guardian.Phone != null,
                        CanReceiveEmail = g.CanReceiveEmail && guardian.Email != null,
                        CreatedAt = now, CreatedBy = user.UserId
                    });
                    await db.SaveChangesAsync(token);
                }

                return new ApiResponse<RunningStudentCreatedDto>
                {
                    Success = true, StatusCode = 201, Message = "Student and enrollment saved.",
                    Data = new RunningStudentCreatedDto
                    {
                        StudentReference = reference, StudentCode = student.StudentCode,
                        FullName = name, EnrollmentReference = enrolled.Data.Reference,
                        RollNo = roll, GuardianLinked = request.Guardian != null
                    }
                };
            }, IsolationLevel.ReadCommitted, ct);
        }
        catch (RejectEntry ex)
        {
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse(ex.Message, ex.Status is >= 400 and <= 499 ? ex.Status : 409);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse("Student allocation changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Running student entry database conflict for tenant {TenantId}", tenant);
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse("Student or roll conflicts with another entry.", 409);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Running student entry failed for tenant {TenantId}", tenant);
            return ApiResponse<RunningStudentCreatedDto>.ErrorResponse("Student was not saved. Reload and retry.", 500);
        }
    }
}
