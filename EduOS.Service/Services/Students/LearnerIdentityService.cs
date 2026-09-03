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
        if (!_currentUser.IsAuthenticated
            || (!_currentUser.IsTenantAdmin && !_currentUser.IsInRole("AdmissionOfficer")))
        {
            return Error("Authorized admission access is required.", 403);
        }

        if (_currentUser.TenantId <= 0)
            return Error("Tenant context is required.", 403);

        if (request == null)
            return Error("Invalid learner identity request.");

        var purpose = request.Purpose;
        if (request.StudentId <= 0
            || !purpose.HasValue
            || !Enum.IsDefined(purpose.Value)
            || request.RequestedScopes == LearnerDataScope.None
            || (request.RequestedScopes & ~AllowedScopes) != 0
            || !request.RequestedScopes.HasFlag(LearnerDataScope.BasicIdentity))
        {
            return await FailWithAuditAsync(
                request.StudentId > 0 ? request.StudentId : null,
                purpose,
                "INVALID_REQUEST",
                "Invalid learner identity request.",
                cancellationToken);
        }

        if (!_identifierProtector.TryNormalize(
                request.IdentifierType,
                request.IdentifierValue,
                out var normalizedIdentifier))
        {
            return await FailWithAuditAsync(
                request.StudentId,
                purpose,
                "INVALID_IDENTIFIER",
                "The identifier format is invalid.",
                cancellationToken);
        }

        var student = await _students.FirstOrDefaultAsync(x => x.Id == request.StudentId);
        if (student == null)
        {
            return await FailWithAuditAsync(
                request.StudentId,
                purpose,
                "STUDENT_NOT_FOUND",
                "Student not found.",
                cancellationToken,
                404);
        }

        if (string.IsNullOrWhiteSpace(student.FullName)
            || student.DOB.Date < new DateTime(1900, 1, 1)
            || student.DOB.Date > DateTime.UtcNow.Date)
        {
            return await FailWithAuditAsync(
                student.Id,
                purpose,
                "STUDENT_PROFILE_INCOMPLETE",
                "Complete the student's name and date of birth before linking identity.",
                cancellationToken,
                409);
        }

        try
        {
            var lookupDigest = _identifierProtector.ComputeLookupDigest(
                request.IdentifierType,
                normalizedIdentifier);
            var protectedValue = _identifierProtector.Protect(normalizedIdentifier);
            var matchingIdentifier = await _identifiers.FirstOrDefaultAsync(x =>
                x.Type == request.IdentifierType && x.LookupDigest == lookupDigest);
            var currentStudentLink = await _links.FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId && x.StudentId == student.Id);

            if (currentStudentLink != null)
            {
                if (currentStudentLink.Status != StudentPersonLinkStatus.Active)
                {
                    return await DenyWithAuditAsync(
                        currentStudentLink.PersonId,
                        student.Id,
                        purpose.Value,
                        "STUDENT_LINK_NOT_ACTIVE",
                        "The student's identity link requires administrator review.",
                        cancellationToken,
                        409);
                }

                return await HandleAlreadyLinkedStudentAsync(
                    student,
                    currentStudentLink,
                    matchingIdentifier,
                    request.IdentifierType,
                    protectedValue,
                    lookupDigest,
                    purpose.Value,
                    cancellationToken);
            }

            if (matchingIdentifier == null)
            {
                return await CreateIdentityAsync(
                    student,
                    request.IdentifierType,
                    protectedValue,
                    lookupDigest,
                    purpose.Value,
                    cancellationToken);
            }

            var existingTenantLink = await _links.FirstOrDefaultAsync(x =>
                x.TenantId == _currentUser.TenantId
                && x.PersonId == matchingIdentifier.PersonId);
            if (existingTenantLink != null)
            {
                return await DenyWithAuditAsync(
                    matchingIdentifier.PersonId,
                    student.Id,
                    purpose.Value,
                    "TENANT_STUDENT_CONFLICT",
                    "This identity is already linked to another student in the institution.",
                    cancellationToken,
                    409);
            }

            // A tenant's self-entered number is not government verification. It
            // cannot become a cross-institution linkage authority until an approved
            // provider or reviewed evidence marks the identifier verified.
            if (matchingIdentifier.VerificationStatus != IdentifierVerificationStatus.Verified)
            {
                return await DenyWithAuditAsync(
                    matchingIdentifier.PersonId,
                    student.Id,
                    purpose.Value,
                    "IDENTIFIER_VERIFICATION_REQUIRED",
                    "Identity review is required before linking.",
                    cancellationToken,
                    409);
            }

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
            return await FailWithAuditAsync(
                student.Id,
                purpose,
                "PROTECTION_NOT_CONFIGURED",
                "Learner identity protection is temporarily unavailable.",
                cancellationToken,
                503);
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

    private async Task<ApiResponse<LearnerIdentityResultDto>> HandleAlreadyLinkedStudentAsync(
        Student student,
        StudentPersonLink link,
        PersonIdentifier? matchingIdentifier,
        PersonIdentifierType identifierType,
        string protectedValue,
        string lookupDigest,
        LearnerIdentityPurpose purpose,
        CancellationToken cancellationToken)
    {
        var person = await _persons.GetByIdAsync(link.PersonId);
        if (person == null)
        {
            return await DenyWithAuditAsync(
                link.PersonId,
                student.Id,
                purpose,
                "LINK_INTEGRITY_FAILURE",
                "The identity link requires administrator review.",
                cancellationToken,
                409);
        }

        if (matchingIdentifier != null && matchingIdentifier.PersonId != link.PersonId)
        {
            return await DenyWithAuditAsync(
                link.PersonId,
                student.Id,
                purpose,
                "IDENTIFIER_PERSON_CONFLICT",
                "The supplied identifier conflicts with the student's current identity.",
                cancellationToken,
                409);
        }

        if (matchingIdentifier == null)
        {
            await _identifiers.AddAsync(new PersonIdentifier
            {
                PersonId = person.Id,
                Type = identifierType,
                ProtectedValue = protectedValue,
                LookupDigest = lookupDigest,
                VerificationStatus = IdentifierVerificationStatus.Unverified
            });
        }

        await AddAccessLogAsync(
            person.Id,
            student.Id,
            null,
            LearnerIdentityAccessAction.RegisterOrLink,
            LearnerIdentityAccessOutcome.Reused,
            purpose,
            matchingIdentifier == null ? "IDENTIFIER_ADDED" : "IDENTITY_ALREADY_LINKED");
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Success(
            matchingIdentifier == null ? "IdentifierAdded" : "AlreadyLinked",
            person.PublicId,
            "Learner identity is linked.");
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> CreateIdentityAsync(
        Student student,
        PersonIdentifierType identifierType,
        string protectedValue,
        string lookupDigest,
        LearnerIdentityPurpose purpose,
        CancellationToken cancellationToken)
    {
        var person = new Person
        {
            PublicId = Guid.NewGuid(),
            FullName = student.FullName.Trim(),
            DateOfBirth = student.DOB.Date,
            Gender = student.Gender.Trim()
        };
        await _persons.AddAsync(person);
        await _identifiers.AddAsync(new PersonIdentifier
        {
            Person = person,
            Type = identifierType,
            ProtectedValue = protectedValue,
            LookupDigest = lookupDigest,
            VerificationStatus = IdentifierVerificationStatus.Unverified
        });
        await _links.AddAsync(new StudentPersonLink
        {
            TenantId = _currentUser.TenantId,
            Student = student,
            Person = person,
            Status = StudentPersonLinkStatus.Active,
            LinkedAt = DateTime.UtcNow,
            LinkedByUserId = _currentUser.UserId
        });
        await AddAccessLogAsync(
            null,
            student.Id,
            null,
            LearnerIdentityAccessAction.RegisterOrLink,
            LearnerIdentityAccessOutcome.Created,
            purpose,
            "IDENTITY_CREATED",
            person: person);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Success("Created", person.PublicId, "Learner identity created.", 201);
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> CreateOrReuseConsentRequestAsync(
        long personId,
        long studentId,
        LearnerIdentityPurpose purpose,
        LearnerDataScope scopes,
        CancellationToken cancellationToken)
    {
        if (_settings.ConsentRequestLifetimeHours is < 1 or > 720)
        {
            return await FailWithAuditAsync(
                studentId,
                purpose,
                "CONSENT_CONFIGURATION_INVALID",
                "Learner consent is temporarily unavailable.",
                cancellationToken,
                503,
                personId);
        }

        var now = DateTime.UtcNow;
        var request = await _consentRequests.FirstOrDefaultAsync(x =>
            x.TenantId == _currentUser.TenantId
            && x.PersonId == personId
            && x.RequestedStudentId == studentId
            && x.Status == LearnerConsentRequestStatus.Pending
            && x.ExpiresAt > now);

        if (request == null)
        {
            request = new LearnerConsentRequest
            {
                TenantId = _currentUser.TenantId,
                PublicId = Guid.NewGuid(),
                PersonId = personId,
                RequestedStudentId = studentId,
                RequestedByUserId = _currentUser.UserId,
                Purpose = purpose,
                RequestedScopes = scopes,
                Status = LearnerConsentRequestStatus.Pending,
                ExpiresAt = now.AddHours(_settings.ConsentRequestLifetimeHours)
            };
            await _consentRequests.AddAsync(request);
        }

        await AddAccessLogAsync(
            personId,
            studentId,
            request.Id > 0 ? request.Id : null,
            LearnerIdentityAccessAction.RequestConsent,
            LearnerIdentityAccessOutcome.ConsentRequired,
            purpose,
            request.Id > 0 ? "CONSENT_REQUEST_REUSED" : "CONSENT_REQUEST_CREATED",
            consentRequest: request);
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

    private async Task<ApiResponse<LearnerIdentityResultDto>> FailWithAuditAsync(
        long? studentId,
        LearnerIdentityPurpose? purpose,
        string reasonCode,
        string message,
        CancellationToken cancellationToken,
        int statusCode = 400,
        long? personId = null)
    {
        try
        {
            await AddAccessLogAsync(
                personId,
                studentId,
                null,
                LearnerIdentityAccessAction.RegisterOrLink,
                LearnerIdentityAccessOutcome.Failed,
                purpose,
                reasonCode);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist learner identity access failure for tenant {TenantId}", _currentUser.TenantId);
        }

        return Error(message, statusCode);
    }

    private async Task<ApiResponse<LearnerIdentityResultDto>> DenyWithAuditAsync(
        long? personId,
        long studentId,
        LearnerIdentityPurpose purpose,
        string reasonCode,
        string message,
        CancellationToken cancellationToken,
        int statusCode)
    {
        await AddAccessLogAsync(
            personId,
            studentId,
            null,
            LearnerIdentityAccessAction.RegisterOrLink,
            LearnerIdentityAccessOutcome.Denied,
            purpose,
            reasonCode);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Error(message, statusCode);
    }

    private Task AddAccessLogAsync(
        long? personId,
        long? studentId,
        long? consentRequestId,
        LearnerIdentityAccessAction action,
        LearnerIdentityAccessOutcome outcome,
        LearnerIdentityPurpose? purpose,
        string reasonCode,
        Person? person = null,
        LearnerConsentRequest? consentRequest = null)
    {
        return _accessLogs.AddAsync(new LearnerIdentityAccessLog
        {
            TenantId = _currentUser.TenantId,
            PersonId = personId,
            StudentId = studentId,
            ConsentRequestId = consentRequestId,
            UserId = _currentUser.UserId,
            Action = action,
            Outcome = outcome,
            Purpose = purpose,
            ReasonCode = reasonCode,
            IpAddress = Truncate(_currentUser.IpAddress, 64),
            UserAgent = Truncate(_currentUser.UserAgent, 500),
            Person = person,
            ConsentRequest = consentRequest
        });
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim()[..Math.Min(value.Trim().Length, maxLength)];

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

    private static ApiResponse<LearnerIdentityResultDto> Error(
        string message,
        int statusCode = 400) =>
        ApiResponse<LearnerIdentityResultDto>.ErrorResponse(message, statusCode);
}
