using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.IO;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Admission;

public sealed class PublicAdmissionService : IPublicAdmissionService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantModule> _tenantModules;
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AdmissionTest> _tests;
    private readonly IGenericRepository<AdmissionResult> _results;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionApplicantDocument> _documents;
    private readonly IGenericRepository<AcademicYear> _academicYears;
    private readonly IGenericRepository<AcademicTerm> _academicTerms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Class> _academicUnits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IFileUploadService _storage;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly TimeProvider _clock;
    private readonly ILogger<PublicAdmissionService> _logger;

    public PublicAdmissionService(
        IGenericRepository<Tenant> tenants,
        IGenericRepository<TenantModule> tenantModules,
        IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AdmissionTest> tests,
        IGenericRepository<AdmissionResult> results,
        IGenericRepository<AdmissionIntakeForm> forms,
        IGenericRepository<AdmissionApplicantDocument> documents,
        IGenericRepository<AcademicYear> academicYears,
        IGenericRepository<AcademicTerm> academicTerms,
        IGenericRepository<Campus> campuses,
        IGenericRepository<Class> academicUnits,
        IUnitOfWork unitOfWork,
        IFileUploadService storage,
        IHttpContextAccessor httpContextAccessor,
        TimeProvider clock,
        ILogger<PublicAdmissionService> logger)
    {
        _tenants = tenants;
        _tenantModules = tenantModules;
        _applications = applications;
        _tests = tests;
        _results = results;
        _forms = forms;
        _documents = documents;
        _academicYears = academicYears;
        _academicTerms = academicTerms;
        _campuses = campuses;
        _academicUnits = academicUnits;
        _unitOfWork = unitOfWork;
        _storage = storage;
        _httpContextAccessor = httpContextAccessor;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>> GetFormsAsync(string tenantKey, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        if (tenant == null) return ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>.ErrorResponse("Admission portal is unavailable.", 404);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken)) return ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>.ErrorResponse("Admission portal is unavailable.", 404);
        var now = _clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<PublicAdmissionIntakeFormDto> forms = (await _forms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.Status == AdmissionIntakeFormStatus.Published && x.OpensAtUtc <= now && x.ClosesAtUtc >= now)
            .OrderBy(x => x.ClosesAtUtc).ThenBy(x => x.Title).ToListAsync(cancellationToken)).Select(MapPublicForm).ToList();
        return ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>.SuccessResponse(forms);
    }

    public async Task<ApiResponse<PublicAdmissionIntakeFormDto>> GetFormAsync(string tenantKey, Guid formReference, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        if (tenant == null || formReference == Guid.Empty) return ApiResponse<PublicAdmissionIntakeFormDto>.ErrorResponse("Admission form is unavailable.", 404);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken)) return ApiResponse<PublicAdmissionIntakeFormDto>.ErrorResponse("Admission form is unavailable.", 404);
        var form = await FindOpenFormAsync(tenant.Id, formReference, cancellationToken);
        return form == null
            ? ApiResponse<PublicAdmissionIntakeFormDto>.ErrorResponse("Admission form is unavailable.", 404)
            : ApiResponse<PublicAdmissionIntakeFormDto>.SuccessResponse(MapPublicForm(form));
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
            var now = _clock.GetUtcNow().UtcDateTime;
            var forms = await _forms.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.Status == AdmissionIntakeFormStatus.Published && x.OpensAtUtc <= now && x.ClosesAtUtc >= now)
                .OrderBy(x => x.ClosesAtUtc).ThenBy(x => x.Title).ToListAsync(cancellationToken);

            return ApiResponse<AdmissionApplicationOptionsDto>.SuccessResponse(new AdmissionApplicationOptionsDto
            {
                AcademicYears = years,
                AcademicTerms = terms,
                Campuses = campuses,
                AcademicUnits = units,
                OpenForms = forms.Select(MapPublicForm).ToList()
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
            var existing = await _applications.GetQueryable().Include(x => x.AdmissionIntakeForm)
                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (existing != null)
            {
                var replayResponses = "{}";
                if (request.AdmissionFormReference.HasValue)
                {
                    if (existing.AdmissionIntakeForm?.PublicId != request.AdmissionFormReference.Value)
                        return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference was already used for different data.", 409);
                    var responseError = AdmissionIntakeRules.ValidateResponses(AdmissionIntakeRules.ReadFields(existing.AdmissionIntakeForm.FieldsJson), request.CustomResponses, out replayResponses);
                    if (responseError != null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse(responseError);
                }
                else if (existing.AdmissionIntakeFormId.HasValue || request.CustomResponses?.Count > 0)
                {
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference was already used for different data.", 409);
                }
                if (!IsSameRequest(existing, request, applicantName, primaryMobile, guardianMobile, email?.ToLowerInvariant(), language, replayResponses))
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Client request reference was already used for different data.", 409);
                return ApiResponse<AdmissionApplicationCreatedDto>.SuccessResponse(MapCreated(existing), "Application already received.");
            }

            AdmissionIntakeForm? form = null;
            string? customResponsesJson = null;
            if (request.AdmissionFormReference.HasValue)
            {
                form = await FindOpenFormAsync(tenant.Id, request.AdmissionFormReference.Value, cancellationToken);
                if (form == null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Admission form is unavailable.", 404);
                if (form.AcademicYearId != request.AcademicYearId || form.AcademicTermId != request.AcademicTermId || form.CampusId != request.CampusId || form.AcademicUnitId != request.AcademicUnitId)
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Application academic choices do not match the selected admission form.", 409);
                var responseError = AdmissionIntakeRules.ValidateResponses(AdmissionIntakeRules.ReadFields(form.FieldsJson), request.CustomResponses, out var responses);
                if (responseError != null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse(responseError);
                customResponsesJson = responses;
            }
            else if (request.CustomResponses?.Count > 0)
            {
                return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("A configured admission form is required for custom responses.");
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
                AdmissionIntakeFormId = form?.Id,
                CustomResponsesJson = customResponsesJson,
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

    public async Task<ApiResponse<AdmissionApplicantDocumentDto>> UploadDocumentAsync(string tenantKey, Guid reference, AdmissionDocumentUploadDto request, CancellationToken cancellationToken = default)
    {
        var tenant = await ResolveTenantAsync(tenantKey, cancellationToken);
        var file = request?.File;
        if (tenant == null || reference == Guid.Empty || request == null || request.ClientRequestId == Guid.Empty || file == null || !TryNormalizeMobile(request.Mobile, out var mobile))
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Applicant document request is invalid.", 400);
        SetTenantContext(tenant.Id);
        if (!await AdmissionModuleEnabledAsync(tenant.Id, cancellationToken)) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Admission portal is unavailable.", 404);

        var documentType = TrimToNull(request.DocumentType);
        var originalName = Path.GetFileName(file.FileName.Replace('\\', '/'));
        var contentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim().ToLowerInvariant();
        if (documentType == null || documentType.Length > 50 || string.IsNullOrWhiteSpace(originalName) || originalName.Length > 255 || originalName.Any(char.IsControl) || contentType.Length > 100)
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Applicant document metadata is invalid.");

        var application = await _applications.GetQueryable().AsNoTracking().Include(x => x.AdmissionIntakeForm)
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.PublicId == reference && x.PrimaryMobile == mobile, cancellationToken);
        if (application?.AdmissionIntakeForm == null) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Application could not be verified.", 404);
        if (application.Status is AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Rejected or AdmissionApplicationStatus.Withdrawn or AdmissionApplicationStatus.Admitted)
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Documents cannot be changed after the application decision.", 409);

        var requirement = AdmissionIntakeRules.ReadRequirements(application.AdmissionIntakeForm.DocumentRequirementsJson)
            .FirstOrDefault(x => string.Equals(x.DocumentType, documentType, StringComparison.OrdinalIgnoreCase));
        if (requirement == null) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("This document type is not accepted for the selected admission form.", 409);
        var extension = AdmissionIntakeRules.NormalizeExtension(Path.GetExtension(originalName));
        if (!requirement.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("The document file type is not allowed.");
        if (file.Length <= 0 || file.Length > requirement.MaxFileSizeMb * 1024L * 1024L) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("The document file size is outside the configured limit.");

        string hash;
        await using (var stream = file.OpenReadStream()) hash = Convert.ToBase64String(await SHA256.HashDataAsync(stream, cancellationToken));
        var replay = await _documents.GetQueryable().AsNoTracking()
            .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientRequestId == request.ClientRequestId, cancellationToken);
        if (replay != null)
        {
            if (replay.ApplicantId != application.Id || !string.Equals(replay.DocumentType, requirement.DocumentType, StringComparison.Ordinal) || replay.Sha256 != hash || replay.FileSizeBytes != file.Length)
                return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Client request ID was already used for a different document.", 409);
            return ApiResponse<AdmissionApplicantDocumentDto>.SuccessResponse(AdmissionIntakeService.MapDocument(replay), "Document already received.");
        }

        var upload = await _storage.UploadPrivateForTenantAsync(file, $"admissions/{application.PublicId:N}", tenant.Id);
        if (!upload.Success || string.IsNullOrWhiteSpace(upload.FileUrl)) return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse(upload.ErrorMessage ?? "Document upload failed.");
        var storageKey = upload.FileUrl;
        try
        {
            using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var currentApplicationStatus = await _applications.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant.Id && x.Id == application.Id)
                .Select(x => (AdmissionApplicationStatus?)x.Status)
                .FirstOrDefaultAsync(cancellationToken);
            if (!currentApplicationStatus.HasValue || currentApplicationStatus.Value is AdmissionApplicationStatus.Approved or AdmissionApplicationStatus.Rejected or AdmissionApplicationStatus.Withdrawn or AdmissionApplicationStatus.Admitted)
            {
                await _storage.DeletePrivateAsync(storageKey);
                return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Documents cannot be changed after the application decision.", 409);
            }
            var concurrentReplay = await _documents.GetQueryable().AsNoTracking()
                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (concurrentReplay != null)
            {
                scope.Complete();
                await _storage.DeletePrivateAsync(storageKey);
                if (concurrentReplay.ApplicantId != application.Id || concurrentReplay.Sha256 != hash || concurrentReplay.FileSizeBytes != file.Length || !string.Equals(concurrentReplay.DocumentType, requirement.DocumentType, StringComparison.Ordinal))
                    return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Client request ID was already used for a different document.", 409);
                return ApiResponse<AdmissionApplicantDocumentDto>.SuccessResponse(AdmissionIntakeService.MapDocument(concurrentReplay), "Document already received.");
            }

            var current = await _documents.GetQueryable()
                .FirstOrDefaultAsync(x => x.TenantId == tenant.Id && x.ApplicantId == application.Id && x.DocumentType == requirement.DocumentType && x.IsCurrent, cancellationToken);
            if (current?.VerificationStatus == AdmissionDocumentVerificationStatus.Verified)
            {
                await _storage.DeletePrivateAsync(storageKey);
                return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("A verified document of this type already exists.", 409);
            }
            var now = _clock.GetUtcNow().UtcDateTime;
            if (current != null)
            {
                current.IsCurrent = false;
                current.UpdatedAt = now;
            }
            var row = new AdmissionApplicantDocument
            {
                TenantId = tenant.Id,
                PublicId = Guid.NewGuid(),
                ClientRequestId = request.ClientRequestId,
                ApplicantId = application.Id,
                AdmissionIntakeFormId = application.AdmissionIntakeForm.Id,
                DocumentType = requirement.DocumentType,
                OriginalFileName = originalName,
                StorageKey = storageKey,
                ContentType = contentType,
                FileSizeBytes = file.Length,
                Sha256 = hash,
                VerificationStatus = AdmissionDocumentVerificationStatus.Pending,
                IsCurrent = true,
                UploadedAtUtc = now,
                CreatedAt = now,
                CreatedBy = null
            };
            await _documents.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            scope.Complete();
            return new ApiResponse<AdmissionApplicantDocumentDto> { Success = true, StatusCode = 201, Message = "Document uploaded for verification.", Data = AdmissionIntakeService.MapDocument(row) };
        }
        catch (DbUpdateException ex)
        {
            await _storage.DeletePrivateAsync(storageKey);
            _logger.LogWarning(ex, "Conflicting applicant document upload for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Document upload conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            await _storage.DeletePrivateAsync(storageKey);
            _logger.LogWarning(ex, "Serialized applicant document upload aborted for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Document upload conflicts with another update. Reload and try again.", 409);
        }
        catch (Exception ex)
        {
            await _storage.DeletePrivateAsync(storageKey);
            _logger.LogError(ex, "Applicant document upload failed for tenant {TenantId}", tenant.Id);
            return ApiResponse<AdmissionApplicantDocumentDto>.ErrorResponse("Document could not be uploaded.", 500);
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

        var assessment = await _results.GetQueryable().AsNoTracking()
            .Include(x => x.AdmissionTest)
            .Where(x => x.TenantId == tenant.Id
                        && x.ApplicantId == application.Id
                        && x.AdmissionTest != null
                        && x.AdmissionTest.IsPublished)
            .OrderByDescending(x => x.AdmissionTest!.PublishedAtUtc)
            .ThenByDescending(x => x.AdmissionTestId)
            .Select(x => new PublicAdmissionAssessmentStatusDto
            {
                TestName = x.AdmissionTest!.Name,
                TestDate = x.AdmissionTest.TestDate,
                PublishedAtUtc = x.AdmissionTest.PublishedAtUtc,
                ObtainedMarks = x.ObtainedMarks,
                TotalMarks = x.AdmissionTest.TotalMarks,
                IsPassed = x.IsPassed,
                MeritPosition = x.MeritPosition,
                ResultStatus = x.ResultStatus
            })
            .FirstOrDefaultAsync(cancellationToken);
        var documents = await _documents.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == tenant.Id && x.ApplicantId == application.Id && x.IsCurrent)
            .OrderBy(x => x.DocumentType).ToListAsync(cancellationToken);

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
                : null,
            Assessment = assessment,
            Documents = documents.Select(AdmissionIntakeService.MapDocument).ToList()
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

    private async Task<AdmissionIntakeForm?> FindOpenFormAsync(long tenantId, Guid reference, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.PublicId == reference
            && x.Status == AdmissionIntakeFormStatus.Published && x.OpensAtUtc <= now && x.ClosesAtUtc >= now, cancellationToken);
    }

    private static bool IsSameRequest(AdmissionApplicant existing, CreateAdmissionApplicationDto request, string name, string mobile, string? guardianMobile, string? email, string language, string customResponsesJson) =>
        existing.AcademicYearId == request.AcademicYearId && existing.AcademicTermId == request.AcademicTermId && existing.CampusId == request.CampusId
        && existing.AcademicUnitId == request.AcademicUnitId && existing.DateOfBirth.Date == request.DateOfBirth.Date
        && existing.Gender == request.Gender
        && string.Equals(existing.ApplicantName, name, StringComparison.Ordinal)
        && string.Equals(existing.ApplicantNameBangla, TrimToNull(request.ApplicantNameBangla), StringComparison.Ordinal)
        && string.Equals(existing.PrimaryMobile, mobile, StringComparison.Ordinal)
        && string.Equals(existing.Email, email, StringComparison.Ordinal)
        && string.Equals(existing.GuardianName, TrimToNull(request.GuardianName), StringComparison.Ordinal)
        && string.Equals(existing.GuardianRelation, TrimToNull(request.GuardianRelation), StringComparison.Ordinal)
        && string.Equals(existing.GuardianMobile, guardianMobile, StringComparison.Ordinal)
        && string.Equals(existing.PresentAddress, TrimToNull(request.PresentAddress), StringComparison.Ordinal)
        && string.Equals(existing.PermanentAddress, TrimToNull(request.PermanentAddress), StringComparison.Ordinal)
        && string.Equals(existing.PreviousInstitution, TrimToNull(request.PreviousInstitution), StringComparison.Ordinal)
        && existing.PreferredLanguage == language
        && string.Equals(existing.CustomResponsesJson ?? "{}", customResponsesJson, StringComparison.Ordinal);

    private static PublicAdmissionIntakeFormDto MapPublicForm(AdmissionIntakeForm x) => new()
    {
        Reference = x.PublicId,
        Code = x.Code,
        Title = x.Title,
        Description = x.Description,
        AcademicYearId = x.AcademicYearId,
        AcademicTermId = x.AcademicTermId,
        CampusId = x.CampusId,
        AcademicUnitId = x.AcademicUnitId,
        OpensAtUtc = x.OpensAtUtc,
        ClosesAtUtc = x.ClosesAtUtc,
        ApplicationFee = x.ApplicationFee,
        Currency = x.Currency,
        Fields = AdmissionIntakeRules.ReadFields(x.FieldsJson),
        DocumentRequirements = AdmissionIntakeRules.ReadRequirements(x.DocumentRequirementsJson)
    };

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
