using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionApplicationService : IAdmissionApplicationService
{
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<AcademicTerm> _academicTerms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Class> _academicUnits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionApplicationService> _logger;

    public AdmissionApplicationService(
        IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<AcademicTerm> academicTerms,
        IGenericRepository<Campus> campuses,
        IGenericRepository<Class> academicUnits,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        TimeProvider clock,
        ILogger<AdmissionApplicationService> logger)
    {
        _applications = applications;
        _academicYears = academicYears;
        _academicTerms = academicTerms;
        _campuses = campuses;
        _academicUnits = academicUnits;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationOptionsDto>();

        var tenantId = _currentUser.TenantId;
        try
        {
            var years = await _academicYears.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderByDescending(x => x.StartDate)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken);
            var yearIds = years.Select(x => x.Id).ToArray();
            var terms = await _academicTerms.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive && yearIds.Contains(x.AcademicYearId))
                .OrderBy(x => x.DisplayOrder)
                .ThenBy(x => x.Name)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name, ParentId = x.AcademicYearId })
                .ToListAsync(cancellationToken);
            var campuses = await _campuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderByDescending(x => x.IsHeadOffice)
                .ThenBy(x => x.Name)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken);
            var units = await _academicUnits.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.IsActive)
                .OrderBy(x => x.NumericValue)
                .ThenBy(x => x.Name)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken);

            return ApiResponse<AdmissionApplicationOptionsDto>.SuccessResponse(new AdmissionApplicationOptionsDto
            {
                AcademicYears = years,
                AcademicTerms = terms,
                Campuses = campuses,
                AcademicUnits = units
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission options failed for tenant {TenantId}", tenantId);
            return ApiResponse<AdmissionApplicationOptionsDto>.ErrorResponse("Admission options could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<PagedResult<AdmissionApplicationListItemDto>>> GetPageAsync(
        AdmissionApplicationQueryDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<PagedResult<AdmissionApplicationListItemDto>>();
        if (request.Page < 1 || request.PageSize is < 1 or > 100)
            return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Pagination is invalid.");
        if (request.Status.HasValue && !Enum.IsDefined(request.Status.Value))
            return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Application status is invalid.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var query = _applications.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenantId);
            var search = TrimToNull(request.Search);
            if (search != null)
                query = query.Where(x => x.ApplicationNumber.Contains(search) || x.ApplicantName.Contains(search));
            if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
            if (request.AcademicYearId.HasValue) query = query.Where(x => x.AcademicYearId == request.AcademicYearId.Value);
            if (request.CampusId.HasValue) query = query.Where(x => x.CampusId == request.CampusId.Value);
            if (request.AcademicUnitId.HasValue) query = query.Where(x => x.AcademicUnitId == request.AcademicUnitId.Value);

            var total = await query.CountAsync(cancellationToken);
            var records = await query
                .Include(x => x.AcademicYear)
                .Include(x => x.Campus)
                .Include(x => x.AcademicUnit)
                .OrderByDescending(x => x.SubmittedAtUtc)
                .ThenByDescending(x => x.Id)
                .Skip((request.Page - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToListAsync(cancellationToken);

            return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.SuccessResponse(new PagedResult<AdmissionApplicationListItemDto>
            {
                Items = records.Select(MapListItem).ToList(),
                TotalCount = total,
                Page = request.Page,
                PageSize = request.PageSize
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission list failed for tenant {TenantId}", tenantId);
            return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Applications could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionApplicationDetailsDto>> GetByReferenceAsync(
        Guid reference,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationDetailsDto>();
        if (reference == Guid.Empty) return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application reference is invalid.");

        var application = await FindDetailsAsync(reference, cancellationToken);
        return application == null
            ? ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application not found.", 404)
            : ApiResponse<AdmissionApplicationDetailsDto>.SuccessResponse(MapDetails(application));
    }

    public async Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(
        CreateAdmissionApplicationDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationCreatedDto>();
        if (request.ClientRequestId == Guid.Empty)
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference is required.");

        var now = _clock.GetUtcNow().UtcDateTime;
        if (request.DateOfBirth.Date < new DateTime(1900, 1, 1) || request.DateOfBirth.Date > now.Date)
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Date of birth is invalid.");
        if (!Enum.IsDefined(request.Gender))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Gender is invalid.");
        if (!TryNormalizeMobile(request.PrimaryMobile, out var primaryMobile))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Primary mobile number is invalid.");
        if (!TryNormalizeOptionalMobile(request.GuardianMobile, out var guardianMobile))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Guardian mobile number is invalid.");

        var applicantName = TrimToNull(request.ApplicantName);
        if (applicantName == null || applicantName.Length > 200)
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Applicant name is required.");
        if (!OptionalLengthsAreValid(request))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("One or more application fields are too long.");
        var email = TrimToNull(request.Email);
        if (email != null && (email.Length > 254 || !MailAddress.TryCreate(email, out var parsedEmail)
                              || !string.Equals(parsedEmail.Address, email, StringComparison.OrdinalIgnoreCase)))
        {
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Email address is invalid.");
        }
        var isMinor = request.DateOfBirth.Date > now.Date.AddYears(-18);
        if (isMinor && (TrimToNull(request.GuardianName) == null
                        || TrimToNull(request.GuardianRelation) == null
                        || guardianMobile == null))
        {
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Guardian name, relation and mobile are required for a minor applicant.");
        }

        var language = request.PreferredLanguage?.Trim();
        if (language is not ("en-BD" or "bn-BD"))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Preferred language is invalid.");

        var tenantId = _currentUser.TenantId;
        try
        {
            var existing = await _applications.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId);
            if (existing != null)
            {
                if (!IsSameRequest(existing, request, applicantName, primaryMobile))
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference was already used for different data.", 409);
                return ApiResponse<AdmissionApplicationCreatedDto>.SuccessResponse(MapCreated(existing), "Application already received.");
            }

            var referenceError = await ValidateReferencesAsync(request, tenantId);
            if (referenceError != null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse(referenceError, 409);

            var publicId = Guid.NewGuid();
            var application = new AdmissionApplicant
            {
                TenantId = tenantId,
                PublicId = publicId,
                ClientRequestId = request.ClientRequestId,
                ApplicationNumber = $"APP-{now:yyyy}-{publicId:N}"[..17].ToUpperInvariant(),
                AcademicYearId = request.AcademicYearId,
                AcademicTermId = request.AcademicTermId,
                CampusId = request.CampusId,
                AcademicUnitId = request.AcademicUnitId,
                ApplicantName = applicantName,
                ApplicantNameBangla = TrimToNull(request.ApplicantNameBangla),
                DateOfBirth = request.DateOfBirth.Date,
                Gender = request.Gender,
                PrimaryMobile = primaryMobile,
                Email = email?.ToLowerInvariant(),
                GuardianName = TrimToNull(request.GuardianName),
                GuardianRelation = TrimToNull(request.GuardianRelation),
                GuardianMobile = guardianMobile,
                PresentAddress = TrimToNull(request.PresentAddress),
                PermanentAddress = TrimToNull(request.PermanentAddress),
                PreviousInstitution = TrimToNull(request.PreviousInstitution),
                PreferredLanguage = language,
                Status = AdmissionApplicationStatus.Submitted,
                SubmittedAtUtc = now,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _applications.AddAsync(application);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return new ApiResponse<AdmissionApplicationCreatedDto>
            {
                Success = true,
                Message = "Application submitted.",
                Data = MapCreated(application),
                StatusCode = 201
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting admission intake for tenant {TenantId}", tenantId);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("The application conflicts with another request. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission intake failed for tenant {TenantId}", tenantId);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Application could not be submitted.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionApplicationDetailsDto>> ReviewAsync(
        Guid reference,
        ReviewAdmissionApplicationDto request,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationDetailsDto>();
        if (reference == Guid.Empty || !Enum.IsDefined(request.Status))
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Review request is invalid.");
        if (request.DecisionNote?.Length > 1000)
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Decision note is too long.");
        if (request.Status is AdmissionApplicationStatus.Rejected or AdmissionApplicationStatus.Withdrawn
            && TrimToNull(request.DecisionNote) == null)
        {
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("A decision note is required for rejection or withdrawal.");
        }

        byte[] expectedVersion;
        try { expectedVersion = Convert.FromBase64String(request.RowVersion ?? string.Empty); }
        catch (FormatException) { return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Row version is invalid."); }

        var tenantId = _currentUser.TenantId;
        try
        {
            var application = await _applications.GetQueryable()
                .Include(x => x.AcademicYear)
                .Include(x => x.AcademicTerm)
                .Include(x => x.Campus)
                .Include(x => x.AcademicUnit)
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == reference, cancellationToken);
            if (application == null) return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application not found.", 404);
            if (!VersionsMatch(application.RowVersion, expectedVersion))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("The application was changed by another user. Reload and try again.", 409);
            if (!CanTransition(application.Status, request.Status))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("This status change is not allowed.", 409);

            application.Status = request.Status;
            application.DecisionNote = TrimToNull(request.DecisionNote);
            application.ReviewedAtUtc = _clock.GetUtcNow().UtcDateTime;
            application.ReviewedByUserId = _currentUser.UserId;
            application.UpdatedAt = application.ReviewedAtUtc;
            application.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionApplicationDetailsDto>.SuccessResponse(MapDetails(application), "Application decision saved.");
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrent admission review {Reference} for tenant {TenantId}", reference, tenantId);
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("The application was changed by another user. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission review failed for {Reference} in tenant {TenantId}", reference, tenantId);
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application decision could not be saved.", 500);
        }
    }

    private async Task<string?> ValidateReferencesAsync(CreateAdmissionApplicationDto request, long tenantId)
    {
        if (!await _academicYears.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicYearId && x.IsActive))
            return "Academic year is unavailable.";
        if (!await _campuses.AnyAsync(x => x.TenantId == tenantId && x.Id == request.CampusId && x.IsActive))
            return "Campus is unavailable.";
        if (!await _academicUnits.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicUnitId && x.IsActive))
            return "Academic unit is unavailable.";
        if (request.AcademicTermId.HasValue
            && !await _academicTerms.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTermId.Value
                                                && x.AcademicYearId == request.AcademicYearId && x.IsActive))
        {
            return "Academic term does not belong to the selected year.";
        }
        return null;
    }

    private async Task<AdmissionApplicant?> FindDetailsAsync(Guid reference, CancellationToken cancellationToken) =>
        await _applications.GetQueryable().AsNoTracking()
            .Include(x => x.AcademicYear)
            .Include(x => x.AcademicTerm)
            .Include(x => x.Campus)
            .Include(x => x.AcademicUnit)
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.PublicId == reference, cancellationToken);

    private bool CanManage() => _currentUser.TenantId > 0
                                && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));

    private static ApiResponse<T> Denied<T>() => ApiResponse<T>.ErrorResponse("Admission officer access is required.", 403);

    private static bool CanTransition(AdmissionApplicationStatus current, AdmissionApplicationStatus next) => current switch
    {
        AdmissionApplicationStatus.Submitted => next is AdmissionApplicationStatus.UnderReview or AdmissionApplicationStatus.Withdrawn,
        AdmissionApplicationStatus.UnderReview => next is AdmissionApplicationStatus.Waitlisted or AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Rejected,
        AdmissionApplicationStatus.Waitlisted => next is AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Rejected,
        _ => false
    };

    private static bool VersionsMatch(byte[] actual, byte[] expected) => actual.Length == expected.Length
        && CryptographicOperations.FixedTimeEquals(actual, expected);

    private static bool IsSameRequest(AdmissionApplicant existing, CreateAdmissionApplicationDto request, string name, string mobile) =>
        existing.AcademicYearId == request.AcademicYearId
        && existing.CampusId == request.CampusId
        && existing.AcademicUnitId == request.AcademicUnitId
        && existing.DateOfBirth.Date == request.DateOfBirth.Date
        && string.Equals(existing.ApplicantName, name, StringComparison.Ordinal)
        && string.Equals(existing.PrimaryMobile, mobile, StringComparison.Ordinal);

    private static AdmissionApplicationCreatedDto MapCreated(AdmissionApplicant x) => new()
    {
        Reference = x.PublicId,
        ApplicationNumber = x.ApplicationNumber,
        Status = x.Status,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static AdmissionApplicationListItemDto MapListItem(AdmissionApplicant x) => new()
    {
        Reference = x.PublicId,
        ApplicationNumber = x.ApplicationNumber,
        ApplicantName = x.ApplicantName,
        MaskedMobile = MaskMobile(x.PrimaryMobile),
        AcademicYearId = x.AcademicYearId,
        AcademicYearName = x.AcademicYear?.Name ?? string.Empty,
        CampusId = x.CampusId,
        CampusName = x.Campus?.Name ?? string.Empty,
        AcademicUnitId = x.AcademicUnitId,
        AcademicUnitName = x.AcademicUnit?.Name ?? string.Empty,
        Status = x.Status,
        SubmittedAtUtc = x.SubmittedAtUtc,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static AdmissionApplicationDetailsDto MapDetails(AdmissionApplicant x)
    {
        var item = MapListItem(x);
        return new AdmissionApplicationDetailsDto
        {
            Reference = item.Reference,
            ApplicationNumber = item.ApplicationNumber,
            ApplicantName = item.ApplicantName,
            MaskedMobile = item.MaskedMobile,
            AcademicYearId = item.AcademicYearId,
            AcademicYearName = item.AcademicYearName,
            CampusId = item.CampusId,
            CampusName = item.CampusName,
            AcademicUnitId = item.AcademicUnitId,
            AcademicUnitName = item.AcademicUnitName,
            Status = item.Status,
            SubmittedAtUtc = item.SubmittedAtUtc,
            RowVersion = item.RowVersion,
            ApplicantNameBangla = x.ApplicantNameBangla,
            DateOfBirth = x.DateOfBirth,
            Gender = x.Gender,
            PrimaryMobile = x.PrimaryMobile,
            Email = x.Email,
            GuardianName = x.GuardianName,
            GuardianRelation = x.GuardianRelation,
            GuardianMobile = x.GuardianMobile,
            PresentAddress = x.PresentAddress,
            PermanentAddress = x.PermanentAddress,
            PreviousInstitution = x.PreviousInstitution,
            PreferredLanguage = x.PreferredLanguage,
            AcademicTermId = x.AcademicTermId,
            AcademicTermName = x.AcademicTerm?.Name,
            ReviewedAtUtc = x.ReviewedAtUtc,
            DecisionNote = x.DecisionNote
        };
    }

    private static string MaskMobile(string value) => value.Length <= 4
        ? new string('*', value.Length)
        : string.Concat(new string('*', value.Length - 4), value[^4..]);

    private static bool TryNormalizeOptionalMobile(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (!TryNormalizeMobile(value, out var result)) return false;
        normalized = result;
        return true;
    }

    private static bool TryNormalizeMobile(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var ascii = value.Trim().Select(ToAsciiDigit).ToArray();
        if (ascii.Any(c => !char.IsDigit(c) && c is not ('+' or ' ' or '-' or '(' or ')'))) return false;
        var compact = new string(ascii.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (compact.Count(c => c == '+') > 1 || compact.Contains('+') && !compact.StartsWith('+')) return false;
        if (compact.StartsWith("00", StringComparison.Ordinal)) compact = "+" + compact[2..];
        else if (compact.StartsWith("880", StringComparison.Ordinal)) compact = "+" + compact;
        else if (compact.Length == 11 && compact.StartsWith("01", StringComparison.Ordinal)) compact = "+88" + compact;
        if (!compact.StartsWith('+')) return false;
        var digits = compact[1..];
        if (digits.Length is < 8 or > 15 || digits[0] == '0' || digits.Any(c => !char.IsDigit(c))) return false;
        normalized = compact;
        return true;
    }

    private static char ToAsciiDigit(char value)
    {
        const string banglaDigits = "০১২৩৪৫৬৭৮৯";
        var index = banglaDigits.IndexOf(value);
        return index >= 0 ? (char)('0' + index) : value;
    }

    private static string? TrimToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool OptionalLengthsAreValid(CreateAdmissionApplicationDto request) =>
        Within(request.ApplicantNameBangla, 200)
        && Within(request.GuardianName, 200)
        && Within(request.GuardianRelation, 50)
        && Within(request.PresentAddress, 1000)
        && Within(request.PermanentAddress, 1000)
        && Within(request.PreviousInstitution, 200);

    private static bool Within(string? value, int maximum) => value == null || value.Trim().Length <= maximum;
}
