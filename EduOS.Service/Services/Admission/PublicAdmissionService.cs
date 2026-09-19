using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mail;

namespace EduOS.Service.Services.Admission;

public sealed class PublicAdmissionService : IPublicAdmissionService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantModule> _tenantModules;
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<AcademicTerm> _academicTerms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Class> _academicUnits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _clock;
    private readonly ILogger<PublicAdmissionService> _logger;

    public PublicAdmissionService(
        IGenericRepository<Tenant> tenants,
        IGenericRepository<TenantModule> tenantModules,
        IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<AcademicTerm> academicTerms,
        IGenericRepository<Campus> campuses,
        IGenericRepository<Class> academicUnits,
        IUnitOfWork unitOfWork,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider clock,
        ILogger<PublicAdmissionService> logger)
    {
        _tenants = tenants;
        _tenantModules = tenantModules;
        _applications = applications;
        _academicYears = academicYears;
        _academicTerms = academicTerms;
        _campuses = campuses;
        _academicUnits = academicUnits;
        _unitOfWork = unitOfWork;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(string tenantKey, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        if (tenant == null) return ApiResponse<AdmissionApplicationOptionsDto>.ErrorResponse("Admission portal is unavailable.", 404);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken))
            return ApiResponse<AdmissionApplicationOptionsDto>.ErrorResponse("Admission portal is unavailable.", 404);

        try
        {
            var years = await _academicYears.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.IsActive)
                .OrderByDescending(x => x.StartDate)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken);
            var yearIds = years.Select(x => x.Id).ToArray();
            var terms = await _academicTerms.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.IsActive && yearIds.Contains(x.AcademicYearId))
                .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Name)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name, ParentId = x.AcademicYearId })
                .ToListAsync(cancellationToken);
            var campuses = await _campuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.IsActive)
                .OrderByDescending(x => x.IsHeadOffice).ThenBy(x => x.Name)
                .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name })
                .ToListAsync(cancellationToken);
            var units = await _academicUnits.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.IsActive)
                .OrderBy(x => x.NumericValue).ThenBy(x => x.Name)
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
            _logger.LogError(ex, "Public admission options failed for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicationOptionsDto>.ErrorResponse("Admission options could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(string tenantKey, CreateAdmissionApplicationDto request, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        if (tenant == null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Admission portal is unavailable.", 404);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Admission portal is unavailable.", 404);
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
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Email address is invalid.");

        var isMinor = request.DateOfBirth.Date > now.Date.AddYears(-18);
        if (isMinor && (TrimToNull(request.GuardianName) == null || TrimToNull(request.GuardianRelation) == null || guardianMobile == null))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Guardian name, relation and mobile are required for a minor applicant.");
        var language = request.PreferredLanguage?.Trim();
        if (language is not ("en-BD" or "bn-BD"))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Preferred language is invalid.");

        try
        {
            var existing = await _applications.FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientRequestId == request.ClientRequestId);
            if (existing != null)
            {
                if (!IsSameRequest(existing, request, applicantName, primaryMobile))
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference was already used for different data.", 409);
                return ApiResponse<AdmissionApplicationCreatedDto>.SuccessResponse(MapCreated(existing), "Application already received.");
            }

            var referenceError = await ValidateReferencesAsync(request, tenant.Id);
            if (referenceError != null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse(referenceError, 409);

            var publicId = Guid.NewGuid();
            var application = new AdmissionApplicant
            {
                TenantId = tenant.Id,
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
                CreatedBy = null
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
            _logger.LogWarning(ex, "Conflicting public admission intake for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("The application conflicts with another request. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Public admission intake failed for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Application could not be submitted.", 500);
        }
    }

    public async Task<ApiResponse<PublicAdmissionStatusDto>> GetStatusAsync(string tenantKey, Guid reference, string mobile, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        if (tenant == null || reference == Guid.Empty || !TryNormalizeMobile(mobile, out var normalizedMobile))
            return ApiResponse<PublicAdmissionStatusDto>.ErrorResponse("Application could not be verified.", 404);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken))
            return ApiResponse<PublicAdmissionStatusDto>.ErrorResponse("Application could not be verified.", 404);

        var application = await _applications.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.PublicId == reference && x.PrimaryMobile == normalizedMobile, cancellationToken);
        if (application == null) return ApiResponse<PublicAdmissionStatusDto>.ErrorResponse("Application could not be verified.", 404);

        return ApiResponse<PublicAdmissionStatusDto>.SuccessResponse(new PublicAdmissionStatusDto
        {
            Reference = application.PublicId,
            ApplicationNumber = application.ApplicationNumber,
            ApplicantName = application.ApplicantName,
            MaskedMobile = MaskMobile(application.PrimaryMobile),
            Status = application.Status,
            SubmittedAtUtc = application.SubmittedAtUtc,
            DecisionNote = application.Status is AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Rejected or AdmissionApplicationStatus.Waitlisted
                ? application.DecisionNote
                : null
        });
    }

    private async Task<Tenant?> ResolveTenantAsync(string tenantKey, CancellationToken cancellationToken)
    {
        var key = TrimToNull(tenantKey);
        if (key == null || key.Length > 200) return null;
        return await _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.IsActive && x.IsOnboardingComplete
                                      && (x.Code == key || x.Subdomain == key || x.CustomDomain == key), cancellationToken);
    }

    private void SetTenantContext(long tenantId)
    {
        var context = _httpContextAccessor.HttpContext;
        if (context != null) context.Items["TenantId"] = tenantId;
    }

    private async Task<bool> AdmissionModuleEnabledAsync(long tenantId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return await _tenantModules.GetQueryable().AsNoTracking()
            .Include(x => x.ProductModule)
            .AnyAsync(x => x.TenantId == tenantId && x.IsEnabled && x.ProductModule != null && x.ProductModule.IsActive
                           && x.ProductModule.Code == "ADMISSION"
                           && (!x.EffectiveFromUtc.HasValue || x.EffectiveFromUtc <= now)
                           && (!x.EffectiveUntilUtc.HasValue || x.EffectiveUntilUtc >= now), cancellationToken);
    }

    private async Task<string?> ValidateReferencesAsync(CreateAdmissionApplicationDto request, long tenantId)
    {
        if (!await _academicYears.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicYearId && x.IsActive)) return "Academic year is unavailable.";
        if (!await _campuses.AnyAsync(x => x.TenantId == tenantId && x.Id == request.CampusId && x.IsActive)) return "Campus is unavailable.";
        if (!await _academicUnits.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicUnitId && x.IsActive)) return "Academic unit is unavailable.";
        if (request.AcademicTermId.HasValue && !await _academicTerms.AnyAsync(x => x.TenantId == tenantId && x.Id == request.AcademicTermId.Value
                                                                              && x.AcademicYearId == request.AcademicYearId && x.IsActive))
            return "Academic term does not belong to the selected year.";
        return null;
    }

    private static bool IsSameRequest(AdmissionApplicant existing, CreateAdmissionApplicationDto request, string name, string mobile) =>
        existing.AcademicYearId == request.AcademicYearId && existing.CampusId == request.CampusId
        && existing.AcademicUnitId == request.AcademicUnitId && existing.DateOfBirth.Date == request.DateOfBirth.Date
        && string.Equals(existing.ApplicantName, name, StringComparison.Ordinal)
        && string.Equals(existing.PrimaryMobile, mobile, StringComparison.Ordinal);

    private static AdmissionApplicationCreatedDto MapCreated(AdmissionApplicant x) => new()
    {
        Reference = x.PublicId,
        ApplicationNumber = x.ApplicationNumber,
        Status = x.Status,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private static string MaskMobile(string value) => value.Length <= 4 ? new string('*', value.Length) : string.Concat(new string('*', value.Length - 4), value[^4..]);

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
        Within(request.ApplicantNameBangla, 200) && Within(request.GuardianName, 200) && Within(request.GuardianRelation, 50)
        && Within(request.PresentAddress, 1000) && Within(request.PermanentAddress, 1000) && Within(request.PreviousInstitution, 200);

    private static bool Within(string? value, int maximum) => value == null || value.Trim().Length <= maximum;
}
