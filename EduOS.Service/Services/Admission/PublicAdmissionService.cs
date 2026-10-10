using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Helpers.Storage;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Admission;

public sealed class PublicAdmissionService : IPublicAdmissionService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<TenantModule> _modules;
    private readonly IGenericRepository<ProductModule> _products;
    private readonly IGenericRepository<TenantSubscription> _subscriptions;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionFormField> _fields;
    private readonly IGenericRepository<AdmissionApplicant> _applicants;
    private readonly IGenericRepository<AdmissionApplicantFieldValue> _values;
    private readonly IGenericRepository<AdmissionApplicantGuardian> _guardians;
    private readonly IGenericRepository<AdmissionApplicantDocument> _documents;
    private readonly IGenericRepository<DocumentTypeDefinition> _documentTypes;
    private readonly IGenericRepository<FileAsset> _assets;
    private readonly IGenericRepository<Person> _persons;
    private readonly IGenericRepository<AdmissionResult> _results;
    private readonly IGenericRepository<AdmissionTest> _tests;
    private readonly IGenericRepository<AdmissionDecision> _decisions;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IUnitOfWork _uow;
    private readonly IFileUploadService _storage;
    private readonly IHttpContextAccessor _http;
    private readonly TimeProvider _clock;
    private readonly ILogger<PublicAdmissionService> _logger;

    public PublicAdmissionService(IGenericRepository<Tenant> tenants,
        IGenericRepository<TenantModule> modules, IGenericRepository<ProductModule> products,
        IGenericRepository<TenantSubscription> subscriptions,
        IGenericRepository<AdmissionIntakeForm> forms, IGenericRepository<AdmissionFormField> fields,
        IGenericRepository<AdmissionApplicant> applicants, IGenericRepository<AdmissionApplicantFieldValue> values,
        IGenericRepository<AdmissionApplicantGuardian> guardians,
        IGenericRepository<AdmissionApplicantDocument> documents,
        IGenericRepository<DocumentTypeDefinition> documentTypes, IGenericRepository<FileAsset> assets,
        IGenericRepository<Person> persons, IGenericRepository<AdmissionResult> results,
        IGenericRepository<AdmissionTest> tests, IGenericRepository<AdmissionDecision> decisions,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicTerm> terms,
        IGenericRepository<Campus> campuses, IGenericRepository<AcademicLevel> levels,
        IUnitOfWork unitOfWork, IFileUploadService storage,
        IHttpContextAccessor http, TimeProvider clock, ILogger<PublicAdmissionService> logger)
    {
        _tenants = tenants; _modules = modules; _products = products; _subscriptions = subscriptions;
        _forms = forms; _fields = fields; _applicants = applicants; _values = values;
        _guardians = guardians; _documents = documents; _documentTypes = documentTypes;
        _assets = assets; _persons = persons; _results = results; _tests = tests; _decisions = decisions;
        _years = years; _terms = terms; _campuses = campuses; _levels = levels;
        _uow = unitOfWork; _storage = storage; _http = http; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>> GetFormsAsync(string tenantKey,
        CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null)
            return Error<IReadOnlyList<PublicAdmissionIntakeFormDto>>("Admission portal is unavailable.", 404);
        var now = _clock.GetUtcNow().UtcDateTime;
        var forms = await _forms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant.Id &&
            x.State == AdmissionFormState.Published &&
            (!x.OpensAt.HasValue || x.OpensAt <= now) && (!x.ClosesAt.HasValue || x.ClosesAt >= now))
            .OrderBy(x => x.ClosesAt).ThenBy(x => x.Id).Take(100).ToListAsync(ct);
        var fieldRows = await FieldsAsync(tenant.Id, forms.Select(x => x.Id).ToArray(), ct);
        IReadOnlyList<PublicAdmissionIntakeFormDto> result = forms.Select(x =>
            MapForm(x, fieldRows.Where(f => f.AdmissionIntakeFormId == x.Id))).ToList();
        return ApiResponse<IReadOnlyList<PublicAdmissionIntakeFormDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<PublicAdmissionIntakeFormDto>> GetFormAsync(string tenantKey,
        Guid formReference, CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null || formReference == Guid.Empty)
            return Error<PublicAdmissionIntakeFormDto>("Admission form is unavailable.", 404);
        var form = await FindOpenFormAsync(tenant.Id, formReference, ct);
        if (form == null) return Error<PublicAdmissionIntakeFormDto>("Admission form is unavailable.", 404);
        var fields = await FieldsAsync(tenant.Id, new[] { form.Id }, ct);
        return ApiResponse<PublicAdmissionIntakeFormDto>.SuccessResponse(MapForm(form, fields));
    }

    public async Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(string tenantKey, CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null)
            return Error<AdmissionApplicationOptionsDto>("Admission portal is unavailable.", 404);
        var t = tenant.Id;
        var years = await _years.GetQueryable().AsNoTracking().Where(x => x.TenantId == t && x.IsActive)
            .OrderByDescending(x => x.StartDate).Take(100)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var ids = years.Select(x => x.Id).ToArray();
        var terms = await _terms.GetQueryable().AsNoTracking().Where(x => x.TenantId == t &&
            x.IsActive && ids.Contains(x.AcademicYearId)).Take(200)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, ParentId = x.AcademicYearId, Name = x.Name }).ToListAsync(ct);
        var campuses = await _campuses.GetQueryable().AsNoTracking().Where(x => x.TenantId == t && x.IsActive)
            .OrderBy(x => x.Name).Take(200)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var levels = await _levels.GetQueryable().AsNoTracking().Where(x => x.TenantId == t && x.IsActive)
            .OrderBy(x => x.LevelNo).Take(200)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var forms = await GetFormsAsync(tenantKey, ct);
        return ApiResponse<AdmissionApplicationOptionsDto>.SuccessResponse(new AdmissionApplicationOptionsDto
        {
            AcademicYears = years, AcademicTerms = terms, Campuses = campuses, AcademicLevels = levels,
            OpenForms = forms.Success && forms.Data != null ? forms.Data.ToList() : new List<PublicAdmissionIntakeFormDto>()
        });
    }

    public async Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(string tenantKey,
        CreateAdmissionApplicationDto request, CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null)
            return Error<AdmissionApplicationCreatedDto>("Admission portal is unavailable.", 404);
        if (request == null || request.ClientRequestId == Guid.Empty || request.AdmissionFormReference is null ||
            string.IsNullOrWhiteSpace(request.ApplicantName) || request.ApplicantName.Trim().Length is < 2 or > 200 ||
            !TryNormalizeMobile(request.PrimaryMobile, out var phone) ||
            !TryNormalizeOptionalMobile(request.GuardianMobile, out var guardianPhone) ||
            request.DateOfBirth.Date < new DateTime(1900, 1, 1) ||
            request.DateOfBirth.Date > _clock.GetUtcNow().UtcDateTime.Date ||
            !Enum.IsDefined(request.Gender))
            return Error<AdmissionApplicationCreatedDto>("Application identity or contact details are invalid.");
        if (!string.IsNullOrWhiteSpace(request.PreviousInstitution))
            return Error<AdmissionApplicationCreatedDto>("Previous institution requires a configured custom intake field.");
        if (!string.IsNullOrWhiteSpace(request.PermanentAddress) &&
            !string.IsNullOrWhiteSpace(request.PresentAddress) &&
            request.PermanentAddress.Trim() != request.PresentAddress.Trim())
            return Error<AdmissionApplicationCreatedDto>("Different permanent and present addresses require an additional address form.");
        var now = _clock.GetUtcNow().UtcDateTime;
        var minor = request.DateOfBirth.Date > now.Date.AddYears(-18);
        if (minor && (string.IsNullOrWhiteSpace(request.GuardianName) ||
            string.IsNullOrWhiteSpace(request.GuardianRelation) || guardianPhone == null))
            return Error<AdmissionApplicationCreatedDto>("Guardian name, relationship and mobile are required for a minor.");
        if (request.ApplicantName!.Trim().Length > 200 || request.ApplicantNameBangla?.Length > 200 ||
            request.Email?.Length > 200 || request.GuardianName?.Length > 200 ||
            request.GuardianRelation?.Length > 50 || request.PermanentAddress?.Length > 1000 ||
            request.PresentAddress?.Length > 1000)
            return Error<AdmissionApplicationCreatedDto>("An application field exceeds the allowed length.");
        var form = await FindOpenFormAsync(tenant.Id, request.AdmissionFormReference.Value, ct);
        if (form == null) return Error<AdmissionApplicationCreatedDto>("Admission form is unavailable.", 404);
        if (form.CampusId != request.CampusId || form.AcademicYearId != request.AcademicYearId ||
            form.AcademicTermId != request.AcademicTermId || form.AcademicLevelId != request.AcademicLevelId)
            return Error<AdmissionApplicationCreatedDto>("Academic choices do not match the intake form.", 409);
        var fields = await FieldsAsync(tenant.Id, new[] { form.Id }, ct);
        var rules = fields.Select(MapField).ToList();
        var error = AdmissionIntakeRules.ValidateResponses(rules, request.CustomResponses, out _);
        if (error != null) return Error<AdmissionApplicationCreatedDto>(error);
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var replay = await _applicants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant.Id && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                if (replay.AdmissionIntakeFormId != form.Id || replay.FullName != request.ApplicantName.Trim() ||
                    replay.FullNameBangla != Trim(request.ApplicantNameBangla) ||
                    replay.DateOfBirth != DateOnly.FromDateTime(request.DateOfBirth.Date) ||
                    replay.Gender != request.Gender.ToString() || replay.Phone != phone ||
                    replay.Email != Trim(request.Email) ||
                    replay.Address != (Trim(request.PermanentAddress) ?? Trim(request.PresentAddress)))
                    return Error<AdmissionApplicationCreatedDto>("Request ID was used for different application data.", 409);
                var savedFields = await (from value in _values.GetQueryable().AsNoTracking()
                    join field in _fields.GetQueryable().AsNoTracking()
                        on value.AdmissionFormFieldId equals field.Id
                    where value.TenantId == tenant.Id && field.TenantId == tenant.Id &&
                        value.AdmissionApplicantId == replay.Id
                    select new { field.FieldKey, value.Value }).ToListAsync(ct);
                var normalizedFields = fields.Select(field => new
                {
                    field.FieldKey,
                    Value = request.CustomResponses != null &&
                        request.CustomResponses.TryGetValue(field.FieldKey, out var value)
                        ? Trim(value) : null
                }).Where(x => x.Value != null).ToList();
                if (savedFields.Count != normalizedFields.Count ||
                    normalizedFields.Any(x => !savedFields.Any(saved =>
                        saved.FieldKey == x.FieldKey && saved.Value == x.Value)))
                    return Error<AdmissionApplicationCreatedDto>("Request ID was used for different custom field values.", 409);
                var savedGuardian = await _guardians.GetQueryable().AsNoTracking()
                    .FirstOrDefaultAsync(x => x.TenantId == tenant.Id &&
                        x.AdmissionApplicantId == replay.Id && x.IsPrimary, ct);
                if (savedGuardian == null && !string.IsNullOrWhiteSpace(request.GuardianName) ||
                    savedGuardian != null && (savedGuardian.FullName != Trim(request.GuardianName) ||
                        savedGuardian.RelationCode != (Trim(request.GuardianRelation) ?? "Guardian") ||
                        savedGuardian.Phone != guardianPhone))
                    return Error<AdmissionApplicationCreatedDto>("Request ID was used for different guardian details.", 409);
                tx.Complete();
                return ApiResponse<AdmissionApplicationCreatedDto>.SuccessResponse(MapCreated(replay),
                    "Application already received.");
            }
            Person person = new()
            {
                FullName = request.ApplicantName.Trim(), FullNameBangla = Trim(request.ApplicantNameBangla),
                DateOfBirth = DateOnly.FromDateTime(request.DateOfBirth.Date),
                Gender = request.Gender.ToString(), Phone = phone, Email = Trim(request.Email),
                PreferredLanguage = request.PreferredLanguage is "en-BD" or "bn-BD" ?
                    request.PreferredLanguage : "bn-BD", CreatedAt = now
            };
            await _persons.AddAsync(person);
            await _uow.SaveChangesAsync(ct);
            var refId = Guid.NewGuid();
            var applicant = new AdmissionApplicant
            {
                TenantId = tenant.Id, PublicId = refId, ClientRequestId = request.ClientRequestId,
                AdmissionIntakeFormId = form.Id, PersonId = person.Id,
                ApplicationNumber = ("APP-" + now.ToString("yyyy") + "-" + refId.ToString("N"))[..17].ToUpperInvariant(),
                FullName = request.ApplicantName.Trim(), FullNameBangla = Trim(request.ApplicantNameBangla),
                DateOfBirth = person.DateOfBirth, Gender = person.Gender, Phone = phone, Email = person.Email,
                Address = Trim(request.PermanentAddress) ?? Trim(request.PresentAddress),
                State = AdmissionApplicantState.Submitted, SubmittedAt = now, CreatedAt = now
            };
            await _applicants.AddAsync(applicant);
            await _uow.SaveChangesAsync(ct);
            foreach (var field in fields)
            {
                var supplied = request.CustomResponses?.GetValueOrDefault(field.FieldKey);
                if (string.IsNullOrWhiteSpace(supplied)) continue;
                await _values.AddAsync(new AdmissionApplicantFieldValue
                {
                    TenantId = tenant.Id, AdmissionApplicantId = applicant.Id,
                    AdmissionFormFieldId = field.Id, Value = supplied, CreatedAt = now
                });
            }
            if (!string.IsNullOrWhiteSpace(request.GuardianName))
                await _guardians.AddAsync(new AdmissionApplicantGuardian
                {
                    TenantId = tenant.Id, AdmissionApplicantId = applicant.Id,
                    FullName = request.GuardianName.Trim(),
                    RelationCode = Trim(request.GuardianRelation) ?? "Guardian",
                    Phone = guardianPhone, IsPrimary = true, CreatedAt = now
                });
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return new ApiResponse<AdmissionApplicationCreatedDto>
            {
                Success = true, StatusCode = 201, Message = "Application submitted.",
                Data = MapCreated(applicant)
            };
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Public admission write conflict for tenant {TenantId}", tenant.Id);
            return Error<AdmissionApplicationCreatedDto>("Application was already submitted or conflicts with existing data.", 409);
        }
        catch (TransactionAbortedException)
        { return Error<AdmissionApplicationCreatedDto>("Concurrent submission conflict. Retry with the same request ID.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Public admission submission failed for tenant {TenantId}", tenant.Id);
            return Error<AdmissionApplicationCreatedDto>("Application could not be submitted.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionApplicantDocumentDto>> UploadDocumentAsync(string tenantKey,
        Guid reference, AdmissionDocumentUploadDto request, CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null)
            return Error<AdmissionApplicantDocumentDto>("Admission portal is unavailable.", 404);
        if (reference == Guid.Empty || request?.ClientRequestId == Guid.Empty || request?.File == null ||
            !TryNormalizeMobile(request.Mobile, out var mobile))
            return Error<AdmissionApplicantDocumentDto>("Document request is invalid.");
        var kind = request.DocumentType?.Trim().ToUpperInvariant();
        var file = request.File;
        if (string.IsNullOrWhiteSpace(kind) || kind.Length > 100 ||
            file.Length is <= 0 or > 10_485_760 || file.Content == null || !file.Content.CanRead ||
            string.IsNullOrWhiteSpace(file.FileName))
            return Error<AdmissionApplicantDocumentDto>("Document type or file is invalid.");
        await using var boundedContent = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int count;
        while ((count = await file.Content.ReadAsync(buffer, ct)) != 0)
        {
            if (boundedContent.Length + count > 10_485_760)
                return Error<AdmissionApplicantDocumentDto>("Document exceeds the upload limit.");
            await boundedContent.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        if (boundedContent.Length != file.Length)
            return Error<AdmissionApplicantDocumentDto>("Uploaded file size does not match the request.");
        boundedContent.Position = 0;
        var checkedFile = new FormFile(boundedContent, 0, boundedContent.Length,
            "File", Path.GetFileName(file.FileName))
        {
            Headers = new HeaderDictionary(), ContentType = file.ContentType
        };
        if (!_storage.ValidateFile(checkedFile))
            return Error<AdmissionApplicantDocumentDto>("Document failed size, type or signature validation.");
        if (!await _documentTypes.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant.Id &&
            x.Code == kind && x.IsActive, ct))
            return Error<AdmissionApplicantDocumentDto>("Document type is not configured for this institution.", 409);
        var applicant = await _applicants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant.Id && x.PublicId == reference && x.Phone == mobile, ct);
        if (applicant == null) return Error<AdmissionApplicantDocumentDto>("Applicant could not be verified.", 404);
        if (applicant.State is AdmissionApplicantState.Admitted or AdmissionApplicantState.Rejected or
            AdmissionApplicantState.Withdrawn or AdmissionApplicantState.Qualified)
            return Error<AdmissionApplicantDocumentDto>("Documents cannot be modified after the final decision.", 409);
        string hash;
        await using (var stream = checkedFile.OpenReadStream())
            hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        var replay = await _assets.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant.Id &&
            x.PublicId == request.ClientRequestId, ct);
        if (replay != null)
        {
            if (replay.Sha256 != hash || replay.SizeBytes != file.Length)
                return Error<AdmissionApplicantDocumentDto>("Request ID was already used for different content.", 409);
            var linked = await _documents.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant.Id && x.AdmissionApplicantId == applicant.Id && x.FileAssetId == replay.Id &&
                x.DocumentTypeCode == kind, ct);
            if (linked == null) return Error<AdmissionApplicantDocumentDto>("Document request ID was reused.", 409);
            return ApiResponse<AdmissionApplicantDocumentDto>.SuccessResponse(MapDocument(linked), "Document already received.");
        }
        var prior = await _documents.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant.Id &&
            x.AdmissionApplicantId == applicant.Id && x.DocumentTypeCode == kind)
            .OrderByDescending(x => x.VersionNo).FirstOrDefaultAsync(ct);
        if (prior?.IsVerified == true) return Error<AdmissionApplicantDocumentDto>("A verified document cannot be overwritten.", 409);
        var uploaded = await _storage.UploadPrivateForTenantAsync(checkedFile,
            $"admissions/{applicant.PublicId:N}", tenant.Id);
        if (!uploaded.Success || string.IsNullOrWhiteSpace(uploaded.FileUrl))
            return Error<AdmissionApplicantDocumentDto>(uploaded.ErrorMessage ?? "Upload could not be completed.");
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var now = _clock.GetUtcNow().UtcDateTime;
            var currentState = await _applicants.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant.Id &&
                x.Id == applicant.Id).Select(x => x.State).SingleAsync(ct);
            if (currentState is AdmissionApplicantState.Admitted or AdmissionApplicantState.Qualified or
                AdmissionApplicantState.Rejected or AdmissionApplicantState.Withdrawn)
            {
                await _storage.DeletePrivateAsync(uploaded.FileUrl);
                return Error<AdmissionApplicantDocumentDto>("Document upload is no longer permitted.", 409);
            }
            var asset = new FileAsset
            {
                TenantId = tenant.Id, PublicId = request.ClientRequestId,
                StorageProvider = "PrivateFileSystem", StorageKey = uploaded.FileUrl,
                OriginalFileName = Path.GetFileName(file.FileName),
                ContentType = file.ContentType, SizeBytes = file.Length, Sha256 = hash,
                UploadedAt = now, Visibility = FileVisibility.Private, IsVerifiedSafe = false, CreatedAt = now
            };
            await _assets.AddAsync(asset);
            await _uow.SaveChangesAsync(ct);
            var row = new AdmissionApplicantDocument
            {
                TenantId = tenant.Id, AdmissionApplicantId = applicant.Id, FileAssetId = asset.Id,
                DocumentTypeCode = kind, VersionNo = (prior?.VersionNo ?? 0) + 1,
                IsVerified = false, CreatedAt = now
            };
            await _documents.AddAsync(row);
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return new ApiResponse<AdmissionApplicantDocumentDto>
            {
                Success = true, StatusCode = 201, Message = "Document received for verification.",
                Data = MapDocument(row)
            };
        }
        catch (Exception ex)
        {
            await _storage.DeletePrivateAsync(uploaded.FileUrl);
            _logger.LogError(ex, "Document upload failed for tenant {TenantId}", tenant.Id);
            return Error<AdmissionApplicantDocumentDto>("Document upload could not be saved.", 409);
        }
    }

    public async Task<ApiResponse<PublicAdmissionStatusDto>> GetStatusAsync(string tenantKey,
        Guid reference, string mobile, CancellationToken ct = default)
    {
        var tenant = await ResolveVisibleTenantAsync(tenantKey, ct);
        if (tenant == null || reference == Guid.Empty || !TryNormalizeMobile(mobile, out var phone))
            return Error<PublicAdmissionStatusDto>("Application could not be verified.", 404);
        var a = await _applicants.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant.Id && x.PublicId == reference && x.Phone == phone, ct);
        if (a == null) return Error<PublicAdmissionStatusDto>("Application could not be verified.", 404);
        var assessment = await (from result in _results.GetQueryable().AsNoTracking()
            join test in _tests.GetQueryable().AsNoTracking() on result.AdmissionTestId equals test.Id
            where result.TenantId == tenant.Id && test.TenantId == tenant.Id &&
                result.AdmissionApplicantId == a.Id && test.IsPublished
            orderby test.TestDate descending, test.Id descending
            select new PublicAdmissionAssessmentStatusDto
            {
                TestName = test.Name, TestDate = test.TestDate.ToDateTime(TimeOnly.MinValue),
                PublishedAtUtc = null, ObtainedMarks = result.ObtainedMarks,
                TotalMarks = test.TotalMarks, IsPassed = result.IsPassed,
                MeritPosition = result.MeritPosition,
                ResultStatus = result.IsPassed ? "Passed" : "NotPassed"
            }).FirstOrDefaultAsync(ct);
        var docs = await _documents.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant.Id && x.AdmissionApplicantId == a.Id)
            .OrderByDescending(x => x.VersionNo).Take(100).ToListAsync(ct);
        var note = await _decisions.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant.Id &&
            x.AdmissionApplicantId == a.Id && x.State == AdmissionDecisionState.Rejected)
            .OrderByDescending(x => x.Id).Select(x => x.Note).FirstOrDefaultAsync(ct);
        return ApiResponse<PublicAdmissionStatusDto>.SuccessResponse(new PublicAdmissionStatusDto
        {
            Reference = a.PublicId, ApplicationNumber = a.ApplicationNumber, ApplicantName = a.FullName,
            MaskedMobile = Mask(a.Phone), State = a.State,
            SubmittedAtUtc = a.SubmittedAt ?? DateTime.MinValue,
            DecisionNote = a.State == AdmissionApplicantState.Rejected ? note : null,
            Assessment = assessment, Documents = docs.Select(MapDocument).ToList()
        });
    }

    private async Task<Tenant?> ResolveVisibleTenantAsync(string tenantKey, CancellationToken ct)
    {
        var tenant = await ResolveTenantAsync(tenantKey, ct);
        if (tenant == null) return null;
        // Public endpoints establish a validated tenant scope before querying tenant-filtered
        // subscriptions/modules. Never take TenantId directly from client-supplied input.
        SetTenantContext(tenant.Id);
        return await CanPublishAsync(tenant.Id, ct) ? tenant : null;
    }

    private async Task<Tenant?> ResolveTenantAsync(string tenantKey, CancellationToken ct)
    {
        var key = tenantKey?.Trim();
        if (string.IsNullOrWhiteSpace(key) || key.Length > 255) return null;
        return await _tenants.GetQueryable().IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(x => !x.IsDeleted && x.State == TenantState.Active &&
                x.OnboardingStage == OnboardingStage.Completed && x.OnboardingCompletedAt != null &&
                (x.Code == key || x.Subdomain == key), ct);
    }
    private void SetTenantContext(long tenantId)
    {
        if (_http.HttpContext != null) _http.HttpContext.Items["TenantId"] = tenantId;
    }
    private async Task<bool> CanPublishAsync(long tenantId, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        var subscribed = await _subscriptions.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId &&
            x.StartsAt <= now && x.EndsAt > now &&
            (x.State == SubscriptionState.Active || x.State == SubscriptionState.Trial ||
             x.State == SubscriptionState.Grace), ct);
        if (!subscribed) return false;
        return await (from selected in _modules.GetQueryable().AsNoTracking()
            join module in _products.GetQueryable().AsNoTracking() on selected.ProductModuleId equals module.Id
            where selected.TenantId == tenantId && selected.IsEnabled && module.IsActive &&
                module.Code == "ADMISSION"
            select selected.Id).AnyAsync(ct);
    }
    private async Task<AdmissionIntakeForm?> FindOpenFormAsync(long tenantId, Guid reference, CancellationToken ct)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        return await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.PublicId == reference && x.State == AdmissionFormState.Published &&
            (!x.OpensAt.HasValue || x.OpensAt <= now) && (!x.ClosesAt.HasValue || x.ClosesAt >= now), ct);
    }
    private async Task<List<AdmissionFormField>> FieldsAsync(long tenantId, long[] formIds, CancellationToken ct) =>
        await _fields.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenantId &&
            formIds.Contains(x.AdmissionIntakeFormId) && x.IsActive).OrderBy(x => x.DisplayOrder).ToListAsync(ct);
    private static PublicAdmissionIntakeFormDto MapForm(AdmissionIntakeForm form, IEnumerable<AdmissionFormField> fields) => new()
    {
        Reference = form.PublicId, Title = form.Title, Code = form.Code,
        AcademicYearId = form.AcademicYearId, AcademicTermId = form.AcademicTermId,
        CampusId = form.CampusId, AcademicLevelId = form.AcademicLevelId,
        OpensAtUtc = form.OpensAt ?? DateTime.MinValue, ClosesAtUtc = form.ClosesAt ?? DateTime.MaxValue,
        ApplicationFee = form.ApplicationFee, Currency = form.CurrencyCode,
        Fields = fields.Select(MapField).ToList()
    };
    private static AdmissionFormFieldDto MapField(AdmissionFormField f) => new()
    {
        Id = f.Id, FieldKey = f.FieldKey, Label = f.Label, DataType = f.DataType,
        IsRequired = f.IsRequired, DisplayOrder = f.DisplayOrder, OptionsJson = f.OptionsJson,
        ValidationJson = f.ValidationJson, IsActive = f.IsActive
    };
    private static AdmissionApplicantDocumentDto MapDocument(AdmissionApplicantDocument row) => new()
    {
        Id = row.Id, FileAssetId = row.FileAssetId, DocumentTypeCode = row.DocumentTypeCode,
        VersionNo = row.VersionNo, IsVerified = row.IsVerified, VerifiedByUserId = row.VerifiedByUserId,
        VerifiedAt = row.VerifiedAt, VerificationNote = row.VerificationNote
    };
    private static AdmissionApplicationCreatedDto MapCreated(AdmissionApplicant a) => new()
    {
        Reference = a.PublicId, ApplicationNumber = a.ApplicationNumber,
        State = a.State, RowVersion = Convert.ToBase64String(a.RowVersion)
    };
    private static string Mask(string? mobile) =>
        string.IsNullOrEmpty(mobile) ? "" : mobile.Length < 5 ? "****" :
            new string('*', mobile.Length - 4) + mobile[^4..];
    private static bool TryNormalizeOptionalMobile(string? input, out string? mobile)
    {
        mobile = null;
        if (string.IsNullOrWhiteSpace(input)) return true;
        if (!TryNormalizeMobile(input, out var normalized)) return false;
        mobile = normalized; return true;
    }
    private static bool TryNormalizeMobile(string? input, out string mobile)
    {
        mobile = "";
        if (string.IsNullOrWhiteSpace(input)) return false;
        const string bengaliDigits = "০১২৩৪৫৬৭৮৯";
        var chars = input.Trim().Select(x =>
        {
            var n = bengaliDigits.IndexOf(x);
            return n < 0 ? x : (char)('0' + n);
        }).Where(x => char.IsAsciiDigit(x) || x == '+').ToArray();
        var value = new string(chars);
        if (value.StartsWith("00")) value = "+" + value[2..];
        else if (value.StartsWith("880")) value = "+" + value;
        else if (value.StartsWith("01") && value.Length == 11) value = "+88" + value;
        if (!value.StartsWith('+') || value.Count(x => x == '+') != 1 || value.Length is < 9 or > 16)
            return false;
        mobile = value;
        return true;
    }
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
}
