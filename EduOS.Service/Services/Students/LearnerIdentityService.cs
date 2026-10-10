using System.Globalization;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Student;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduOS.Service.Services.Students;

public sealed class LearnerIdentityService : ILearnerIdentityService
{
    private const LearnerDataScope AllowedScopes =
        LearnerDataScope.BasicIdentity
        | LearnerDataScope.InstitutionMembershipHistory
        | LearnerDataScope.AcademicSummary;

    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<PersonIdentifier> _identifiers;
    private readonly IGenericRepository<StudentPersonLink> _links;
    private readonly IGenericRepository<LearnerConsentRequest> _consentRequests;
    private readonly IGenericRepository<LearnerIdentityAccessLog> _accessLogs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly ILearnerIdentifierProtector _identifierProtector;
    private readonly LearnerIdentitySettings _settings;
    private readonly ILogger<LearnerIdentityService> _logger;

    public LearnerIdentityService(
        IGenericRepository<Student> students,
        IGenericRepository<Person> persons,
        IGenericRepository<PersonIdentifier> identifiers,
        IGenericRepository<StudentPersonLink> links,
        IGenericRepository<LearnerConsentRequest> consentRequests,
        IGenericRepository<LearnerIdentityAccessLog> accessLogs,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        ILearnerIdentifierProtector identifierProtector,
        IOptions<LearnerIdentitySettings> settings,
        ILogger<LearnerIdentityService> logger)
    {
        _students = students;
        _persons = persons;
        _identifiers = identifiers;
        _links = links;
        _consentRequests = consentRequests;
        _accessLogs = accessLogs;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _identifierProtector = identifierProtector;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<ApiResponse<LearnerIdentityResultDto>> RegisterOrRequestAsync(
        RegisterLearnerIdentityRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!_currentUser.IsAuthenticated || (!_currentUser.IsTenantAdmin && !_currentUser.IsInRole("AdmissionOfficer")))
            return Error("Authorized admission access is required.", 403);
        if (_currentUser.TenantId <= 0) return Error("Tenant context is required.", 403);
        if (request == null) return Error("Invalid learner identity request.");

        var purpose = request.Purpose;
        if (request.StudentId <= 0 ||
            !purpose.HasValue ||
            !Enum.IsDefined(purpose.Value) ||
            request.RequestedScopes == LearnerDataScope.None ||
            (request.RequestedScopes & ~AllowedScopes) != 0 ||
            !request.RequestedScopes.HasFlag(LearnerDataScope.BasicIdentity))
            return Error("Invalid learner identity request.");

        if (!_identifierProtector.TryNormalize(request.IdentifierType, request.IdentifierValue, out var normalizedIdentifier))
            return Error("The identifier format is invalid.");

        var student = await _students.GetQueryable()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == request.StudentId && x.StatusCode == "Active" && !x.IsDeleted, cancellationToken);
        if (student == null) return Error("Student not found.", 404);

        if (string.IsNullOrWhiteSpace(student.FullName) ||
            !student.DateOfBirth.HasValue ||
            student.DateOfBirth.Value < new DateOnly(1900, 1, 1) ||
            student.DateOfBirth.Value > DateOnly.FromDateTime(DateTime.UtcNow))
            return await DenyKnownStudentAsync(student, purpose.Value, "STUDENT_PROFILE_INCOMPLETE",
                "Complete the student's name and date of birth before linking identity.", 409, cancellationToken);

        if (!TryMapIdentifierType(request.IdentifierType, out var identifierKind))
            return await DenyKnownStudentAsync(student, purpose.Value, "UNSUPPORTED_IDENTIFIER",
                "The identifier type is not supported by the final identity model.", 400, cancellationToken);

        try
        {
            var digest = _identifierProtector.ComputeLookupDigest(request.IdentifierType, normalizedIdentifier);
            var protectedValue = _identifierProtector.Protect(normalizedIdentifier);
            var matchingIdentifier = await _identifiers.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.IdentifierType == identifierKind && x.LookupDigest == digest, cancellationToken);

            var currentPerson = student.PersonId > 0
                ? await _persons.GetQueryable().FirstOrDefaultAsync(x => x.Id == student.PersonId && !x.IsDeleted, cancellationToken)
                : null;

            if (currentPerson != null)
                return await HandleExistingPersonAsync(student, currentPerson, matchingIdentifier, identifierKind, protectedValue, digest, purpose.Value, cancellationToken);

            if (student.PersonId > 0)
                return await DenyKnownStudentAsync(student, purpose.Value, "PERSON_INTEGRITY_FAILURE",
                    "The learner identity record requires administrator review.", 409, cancellationToken);

            if (matchingIdentifier == null)
                return await CreateIdentityAsync(student, identifierKind, protectedValue, digest, purpose.Value, cancellationToken);

            var existingTenantLink = await _links.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId &&
                                          x.PersonId == matchingIdentifier.PersonId &&
                                          x.UnlinkedAt == null, cancellationToken);
            if (existingTenantLink != null)
                return await DenyWithAuditAsync(matchingIdentifier.PersonId, student.Id, purpose.Value,
                    "TENANT_STUDENT_CONFLICT", "This identity is already linked to another student in the institution.", 409, cancellationToken);

            if (!matchingIdentifier.IsVerified)
                return await DenyWithAuditAsync(matchingIdentifier.PersonId, student.Id, purpose.Value,
                    "IDENTIFIER_VERIFICATION_REQUIRED", "Identity review is required before linking.", 409, cancellationToken);

            return await CreateOrReuseConsentRequestAsync(
                matchingIdentifier.PersonId,
                student.Id,
                purpose.Value,
                request.RequestedScopes,
                cancellationToken);
        }
        catch (LearnerIdentityProtectionException ex)
        {
            _logger.LogError(ex, "Learner identity protection is unavailable for tenant {TenantId}", _currentUser.TenantId);
            return Error("Learner identity protection is temporarily unavailable.", 503);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent learner identity update for tenant {TenantId}", _currentUser.TenantId);
            return Error("The identity was changed by another request. Reload and try again.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting learner identity update for tenant {TenantId}", _currentUser.TenantId);
            return Error("The identity was changed by another request. Try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Learner identity operation failed for tenant {TenantId}", _currentUser.TenantId);
            return Error("Learner identity operation failed.", 500);
        }
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> HandleExistingPersonAsync(
        Student student,
        Person person,
        PersonIdentifier? matchingIdentifier,
        PersonIdentifierKind identifierKind,
        string protectedValue,
        string lookupDigest,
        LearnerIdentityPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (matchingIdentifier != null && matchingIdentifier.PersonId != person.Id)
            return await DenyWithAuditAsync(person.Id, student.Id, purpose, "IDENTIFIER_PERSON_CONFLICT",
                "The supplied identifier conflicts with the student's current identity.", 409, cancellationToken);

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            if (matchingIdentifier == null)
            {
                await _identifiers.AddAsync(new PersonIdentifier
                {
                    PersonId = person.Id,
                    IdentifierType = identifierKind,
                    ProtectedValue = protectedValue,
                    LookupDigest = lookupDigest,
                    IsVerified = false,
                    CreatedBy = _currentUser.UserId,
                    CreatedAt = DateTime.UtcNow
                });
            }

            var activeLink = await _links.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId &&
                                          x.StudentId == student.Id &&
                                          x.PersonId == person.Id &&
                                          x.UnlinkedAt == null, cancellationToken);
            if (activeLink == null)
            {
                await _links.AddAsync(new StudentPersonLink
                {
                    TenantId = _currentUser.TenantId,
                    PersonId = person.Id,
                    StudentId = student.Id,
                    IsPrimary = true,
                    LinkedAt = DateTime.UtcNow,
                    LinkedByUserId = _currentUser.UserId,
                    LinkReason = "Canonical learner identity link",
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = _currentUser.UserId
                });
            }

            await AddAccessLogAsync(person.Id, student.Id, null, "RegisterOrLink",
                matchingIdentifier == null ? "Created" : "Reused", purpose, matchingIdentifier == null ? "IDENTIFIER_ADDED" : "IDENTITY_ALREADY_LINKED");
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Success(matchingIdentifier == null ? "IdentifierAdded" : "AlreadyLinked", person.PublicId, "Learner identity is linked.");
        }, cancellationToken);
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> CreateIdentityAsync(
        Student student,
        PersonIdentifierKind identifierKind,
        string protectedValue,
        string lookupDigest,
        LearnerIdentityPurpose purpose,
        CancellationToken cancellationToken)
    {
        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            var now = DateTime.UtcNow;
            var person = new Person
            {
                PublicId = Guid.NewGuid(),
                FullName = student.FullName.Trim(),
                FullNameBangla = student.FullNameBangla,
                DateOfBirth = student.DateOfBirth,
                Gender = student.Gender,
                Phone = student.Phone,
                Email = student.Email,
                PreferredLanguage = student.PreferredLanguage,
                PhotoUrl = student.PhotoUrl,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _persons.AddAsync(person);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            student.PersonId = person.Id;
            student.PersonDataSnapshotAt = now;
            student.UpdatedAt = now;
            student.UpdatedBy = _currentUser.UserId;
            _students.Update(student);

            await _identifiers.AddAsync(new PersonIdentifier
            {
                PersonId = person.Id,
                IdentifierType = identifierKind,
                ProtectedValue = protectedValue,
                LookupDigest = lookupDigest,
                IsVerified = false,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            });
            await _links.AddAsync(new StudentPersonLink
            {
                TenantId = _currentUser.TenantId,
                PersonId = person.Id,
                StudentId = student.Id,
                IsPrimary = true,
                LinkedAt = now,
                LinkedByUserId = _currentUser.UserId,
                LinkReason = "Learner identity created by authorized admission workflow",
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            });
            await AddAccessLogAsync(person.Id, student.Id, null, "RegisterOrLink", "Created", purpose, "IDENTITY_CREATED");

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Success("Created", person.PublicId, "Learner identity created.", 201);
        }, cancellationToken);
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> CreateOrReuseConsentRequestAsync(
        long personId,
        long studentId,
        LearnerIdentityPurpose purpose,
        LearnerDataScope scopes,
        CancellationToken cancellationToken)
    {
        if (_settings.ConsentRequestLifetimeHours is < 1 or > 720)
            return await DenyWithAuditAsync(personId, studentId, purpose, "CONSENT_CONFIGURATION_INVALID",
                "Learner consent is temporarily unavailable.", 503, cancellationToken);

        var now = DateTime.UtcNow;
        var expiresAt = now.AddHours(_settings.ConsentRequestLifetimeHours);
        var serializedPurpose = purpose.ToString();
        var serializedScopes = ((long)scopes).ToString(CultureInfo.InvariantCulture);

        var request = await _consentRequests.GetQueryable()
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId &&
                                      x.PersonId == personId &&
                                      x.RequestedStudentId == studentId &&
                                      x.State == ConsentState.Pending &&
                                      x.ExpiresAt.HasValue &&
                                      x.ExpiresAt.Value > now, cancellationToken);
        var isNew = request == null;
        if (request == null)
        {
            request = new LearnerConsentRequest
            {
                TenantId = _currentUser.TenantId,
                PublicId = Guid.NewGuid(),
                PersonId = personId,
                RequestedStudentId = studentId,
                RequestedByUserId = _currentUser.UserId,
                Purpose = serializedPurpose,
                RequestedScopes = serializedScopes,
                State = ConsentState.Pending,
                RequestedAt = now,
                ExpiresAt = expiresAt,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _consentRequests.AddAsync(request);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        else if (!string.Equals(request.Purpose, serializedPurpose, StringComparison.OrdinalIgnoreCase) ||
                 request.RequestedScopes != serializedScopes)
        {
            return await DenyWithAuditAsync(personId, studentId, purpose, "CONSENT_REQUEST_CONFLICT",
                "A pending consent request already exists with different scope or purpose.", 409, cancellationToken);
        }

        await AddAccessLogAsync(personId, studentId, request.Id, "RequestConsent", "ConsentRequired", purpose,
            isNew ? "CONSENT_REQUEST_CREATED" : "CONSENT_REQUEST_REUSED");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ApiResponse<LearnerIdentityResultDto>
        {
            Success = true,
            Message = "Consent is required before this identity can be linked.",
            StatusCode = 202,
            Data = new LearnerIdentityResultDto
            {
                State = "ConsentRequired",
                ConsentRequired = true,
                ConsentRequestReference = request.PublicId,
                ConsentRequestExpiresAt = request.ExpiresAt
            }
        };
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> DenyKnownStudentAsync(
        Student student,
        LearnerIdentityPurpose purpose,
        string reasonCode,
        string message,
        int statusCode,
        CancellationToken cancellationToken)
    {
        if (student.PersonId > 0)
            return await DenyWithAuditAsync(student.PersonId, student.Id, purpose, reasonCode, message, statusCode, cancellationToken);

        _logger.LogWarning("Learner identity denied before a person was linked. Tenant {TenantId}, Student {StudentId}, Reason {ReasonCode}",
            _currentUser.TenantId, student.Id, reasonCode);
        return Error(message, statusCode);
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> DenyWithAuditAsync(
        long personId,
        long studentId,
        LearnerIdentityPurpose purpose,
        string reasonCode,
        string message,
        int statusCode,
        CancellationToken cancellationToken)
    {
        await AddAccessLogAsync(personId, studentId, null, "RegisterOrLink", "Denied", purpose, reasonCode);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Error(message, statusCode);
    }

    private Task AddAccessLogAsync(
        long personId,
        long studentId,
        long? consentRequestId,
        string action,
        string outcome,
        LearnerIdentityPurpose purpose,
        string reasonCode)
    {
        return _accessLogs.AddAsync(new LearnerIdentityAccessLog
        {
            TenantId = _currentUser.TenantId,
            PersonId = personId,
            StudentId = studentId,
            LearnerConsentRequestId = consentRequestId,
            UserId = _currentUser.UserId,
            Action = action,
            OutcomeCode = outcome,
            ReasonCode = reasonCode,
            Purpose = purpose.ToString(),
            AccessedAt = DateTime.UtcNow,
            IpAddress = Truncate(_currentUser.IpAddress, 100),
            UserAgent = Truncate(_currentUser.UserAgent, 500),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = _currentUser.UserId
        });
    }

    private static bool TryMapIdentifierType(PersonIdentifierType source, out PersonIdentifierKind target)
    {
        target = source switch
        {
            PersonIdentifierType.BirthRegistration => PersonIdentifierKind.BirthRegistration,
            PersonIdentifierType.NationalId => PersonIdentifierKind.NationalId,
            _ => PersonIdentifierKind.Other
        };
        return target != PersonIdentifierKind.Other;
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed[..Math.Min(trimmed.Length, maxLength)];
    }

    private static ApiResponse<LearnerIdentityResultDto> Success(
        string state,
        Guid personReference,
        string message,
        int statusCode = 200) =>
        new()
        {
            Success = true,
            Message = message,
            StatusCode = statusCode,
            Data = new LearnerIdentityResultDto
            {
                State = state,
                PersonReference = personReference,
                ConsentRequired = false
            }
        };

    private static ApiResponse<LearnerIdentityResultDto> Error(string message, int statusCode = 400) =>
        ApiResponse<LearnerIdentityResultDto>.ErrorResponse(message, statusCode);
}
