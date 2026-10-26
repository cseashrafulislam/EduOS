using System.Data;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Learners;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Learners;

/// <summary>
/// Edits structured identity addresses only for an exclusively tenant-owned Person.
/// Never overwrites historical Student.Address or admission snapshots.
/// </summary>
public sealed class StudentPersonAddressService : IStudentPersonAddressService
{
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<Guardian> _guardians;
    private readonly IGenericRepository<AdmissionApplicant> _applicants;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<StudentPersonLink> _links;
    private readonly IGenericRepository<PersonAddress> _addresses;
    private readonly IGenericRepository<LearnerConsentRequest> _consents;
    private readonly IGenericRepository<LearnerDataGrant> _grants;
    private readonly IGenericRepository<LearnerIdentityAccessLog> _logs;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _user;
    private readonly ILogger<StudentPersonAddressService> _logger;

    public StudentPersonAddressService(
        IGenericRepository<Student> students,
        IGenericRepository<Guardian> guardians,
        IGenericRepository<AdmissionApplicant> applicants,
        IGenericRepository<Person> persons,
        IGenericRepository<StudentPersonLink> links,
        IGenericRepository<PersonAddress> addresses,
        IGenericRepository<LearnerConsentRequest> consents,
        IGenericRepository<LearnerDataGrant> grants,
        IGenericRepository<LearnerIdentityAccessLog> logs,
        IUnitOfWork unitOfWork,
        ICurrentUserService user,
        ILogger<StudentPersonAddressService> logger)
    {
        _students = students;
        _guardians = guardians;
        _applicants = applicants;
        _persons = persons;
        _links = links;
        _addresses = addresses;
        _consents = consents;
        _grants = grants;
        _logs = logs;
        _unitOfWork = unitOfWork;
        _user = user;
        _logger = logger;
    }

    private bool Allowed => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;

    private static bool TryType(string? value, out string type)
    {
        type = string.Equals(value, "Current", StringComparison.OrdinalIgnoreCase) ? "Current"
            : string.Equals(value, "Permanent", StringComparison.OrdinalIgnoreCase) ? "Permanent" : "";
        return type.Length > 0;
    }

    // Fail closed if the global Person has ever been associated with another tenant.
    // This endpoint is not a substitute for cross-institution learner consent.
    private async Task<Student?> ResolveAsync(Guid reference, CancellationToken ct)
    {
        var student = await _students.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == _user.TenantId && x.PublicId == reference &&
                x.StatusCode == "Active" && !x.IsDeleted && x.PersonId > 0, ct);
        if (student == null) return null;
        var personId = student.PersonId;
        if (!await _persons.GetQueryable().AsNoTracking().AnyAsync(x => x.Id == personId && !x.IsDeleted, ct))
            return null;
        if (!await _links.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == _user.TenantId && x.StudentId == student.Id && x.PersonId == personId &&
                !x.IsDeleted && x.UnlinkedAt == null, ct))
            return null;

        if (await _links.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct)
            || await _students.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct)
            || await _guardians.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct)
            || await _applicants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct)
            || await _consents.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct)
            || await _grants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
                .AnyAsync(x => x.PersonId == personId && x.TenantId != _user.TenantId, ct))
            return null;
        return student;
    }

    public async Task<ApiResponse<List<StudentPersonAddressDto>>> GetAsync(Guid studentReference, CancellationToken cancellationToken = default)
    {
        if (!Allowed) return ApiResponse<List<StudentPersonAddressDto>>.ErrorResponse("Tenant administrator access is required.", 403);
        if (studentReference == Guid.Empty) return ApiResponse<List<StudentPersonAddressDto>>.ErrorResponse("Student reference is required.");
        var student = await ResolveAsync(studentReference, cancellationToken);
        if (student == null) return ApiResponse<List<StudentPersonAddressDto>>.ErrorResponse("Student identity is unavailable for address management.", 404);
        var rows = await _addresses.GetQueryable().AsNoTracking()
            .Where(x => x.PersonId == student.PersonId && !x.IsDeleted &&
                (x.AddressTypeCode == "Current" || x.AddressTypeCode == "Permanent"))
            .OrderBy(x => x.AddressTypeCode).Take(3).ToListAsync(cancellationToken);
        if (rows.GroupBy(x => x.AddressTypeCode).Any(x => x.Count() > 1))
            return ApiResponse<List<StudentPersonAddressDto>>.ErrorResponse("Duplicate address records require administrator review.", 409);
        return ApiResponse<List<StudentPersonAddressDto>>.SuccessResponse(rows.Select(ToDto).ToList());
    }

    public async Task<ApiResponse<StudentPersonAddressDto>> SaveAsync(
        Guid studentReference, string addressType, SaveStudentPersonAddressRequestDto request,
        CancellationToken cancellationToken = default)
    {
        if (!Allowed) return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Tenant administrator access is required.", 403);
        if (studentReference == Guid.Empty || !TryType(addressType, out var type) ||
            request == null || request.ClientRequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.AddressLine1) ||
            request.AddressLine1.Trim().Length is < 3 or > 500 ||
            request.AddressLine2?.Length > 500 || request.City?.Length > 100 ||
            request.District?.Length > 100 || request.DivisionOrState?.Length > 100 ||
            request.PostalCode?.Length > 30 ||
            request.CountryCode is null || request.CountryCode.Length != 2 ||
            request.CountryCode.Any(c => c is < 'A' or > 'Z'))
            return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Invalid structured address request.");

        try
        {
            return await _unitOfWork.ExecuteInTransactionAsync(async ct =>
            {
                // Resolve again inside the serializable transaction to prevent stale ownership decisions.
                var student = await ResolveAsync(studentReference, ct);
                if (student == null)
                    return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Student identity is unavailable for address management.", 404);

                var rows = await _addresses.GetQueryable()
                    .Where(x => x.PersonId == student.PersonId && x.AddressTypeCode == type && !x.IsDeleted)
                    .Take(2).ToListAsync(ct);
                if (rows.Count > 1)
                    return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Duplicate address records require administrator review.", 409);
                var row = rows.SingleOrDefault();
                var line1 = request.AddressLine1.Trim();
                var line2 = Clean(request.AddressLine2);
                var city = Clean(request.City);
                var district = Clean(request.District);
                var division = Clean(request.DivisionOrState);
                var postal = Clean(request.PostalCode);
                var country = request.CountryCode;
                bool matches = row != null && row.AddressLine1 == line1 && row.AddressLine2 == line2 &&
                    row.City == city && row.District == district && row.DivisionOrState == division &&
                    row.PostalCode == postal && row.CountryCode == country;

                var requestKey = "ADDRESS:" + request.ClientRequestId.ToString("N");
                var previous = await _logs.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == _user.TenantId &&
                        x.Action == "PersonAddressSaved" && x.ReasonCode == requestKey, ct);
                if (previous != null)
                    return previous.StudentId == student.Id && previous.PersonId == student.PersonId &&
                        previous.Purpose == type && matches
                        ? ApiResponse<StudentPersonAddressDto>.SuccessResponse(ToDto(row!), "Address already saved.")
                        : ApiResponse<StudentPersonAddressDto>.ErrorResponse("This request ID was already used for different address data.", 409);

                if (row == null && !string.IsNullOrEmpty(request.RowVersion))
                    return ApiResponse<StudentPersonAddressDto>.ErrorResponse("The address changed. Reload and retry.", 409);
                if (row != null)
                {
                    if (row.RowVersion.Length > 0)
                    {
                        byte[] supplied;
                        try { supplied = Convert.FromBase64String(request.RowVersion ?? ""); }
                        catch (FormatException) { return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Invalid address version.", 409); }
                        if (!supplied.SequenceEqual(row.RowVersion))
                            return ApiResponse<StudentPersonAddressDto>.ErrorResponse("The address changed. Reload and retry.", 409);
                    }
                    if (matches) return ApiResponse<StudentPersonAddressDto>.SuccessResponse(ToDto(row), "Address is unchanged.");
                }

                var now = DateTime.UtcNow;
                if (row == null)
                {
                    row = new PersonAddress
                    {
                        PersonId = student.PersonId, AddressTypeCode = type,
                        CreatedAt = now, CreatedBy = _user.UserId
                    };
                    await _addresses.AddAsync(row);
                }
                else
                {
                    row.UpdatedAt = now;
                    row.UpdatedBy = _user.UserId;
                }
                row.AddressLine1 = line1;
                row.AddressLine2 = line2;
                row.City = city;
                row.District = district;
                row.DivisionOrState = division;
                row.PostalCode = postal;
                row.CountryCode = country;
                row.IsPrimary = type == "Current";

                await _logs.AddAsync(new LearnerIdentityAccessLog
                {
                    TenantId = _user.TenantId, PersonId = student.PersonId, StudentId = student.Id,
                    UserId = _user.UserId, Action = "PersonAddressSaved",
                    OutcomeCode = rows.Count == 0 ? "Created" : "Updated",
                    ReasonCode = requestKey, Purpose = type, AccessedAt = now,
                    CreatedAt = now, CreatedBy = _user.UserId
                });
                await _unitOfWork.SaveChangesAsync(ct);
                return ApiResponse<StudentPersonAddressDto>.SuccessResponse(ToDto(row), "Structured address saved.");
            }, IsolationLevel.Serializable, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<StudentPersonAddressDto>.ErrorResponse("The address changed. Reload and retry.", 409);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Concurrent structured learner address write for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentPersonAddressDto>.ErrorResponse("The address changed. Reload and retry.", 409);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Structured learner address save failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentPersonAddressDto>.ErrorResponse("Could not save the structured address.", 500);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static StudentPersonAddressDto ToDto(PersonAddress row) => new()
    {
        AddressTypeCode = row.AddressTypeCode,
        AddressLine1 = row.AddressLine1,
        AddressLine2 = row.AddressLine2,
        City = row.City,
        District = row.District,
        DivisionOrState = row.DivisionOrState,
        PostalCode = row.PostalCode,
        CountryCode = row.CountryCode,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };
}
