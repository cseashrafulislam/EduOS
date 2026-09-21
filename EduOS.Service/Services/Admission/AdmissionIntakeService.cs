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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionIntakeService : IAdmissionIntakeService
{
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AdmissionApplicantDocument> _documents;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<Class> _academicUnits;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICurrentUserService _currentUser;
    private readonly IFileUploadService _storage;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionIntakeService> _logger;

    public AdmissionIntakeService(
        IGenericRepository<AdmissionIntakeForm> forms,
        IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AdmissionApplicantDocument> documents,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms,
        IGenericRepository<Campus> campuses,
        IGenericRepository<Class> academicUnits,
        IUnitOfWork unitOfWork,
        ICurrentUserService currentUser,
        IFileUploadService storage,
        TimeProvider clock,
        ILogger<AdmissionIntakeService> logger)
    {
        _forms = forms;
        _applications = applications;
        _documents = documents;
        _years = years;
        _terms = terms;
        _campuses = campuses;
        _academicUnits = academicUnits;
        _unitOfWork = unitOfWork;
        _currentUser = currentUser;
        _storage = storage;
        _clock = clock;
        _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionIntakeFormDto>>> GetFormsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionIntakeFormDto>>();
        var rows = await _forms.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId)
            .OrderByDescending(x => x.OpensAtUtc).ThenBy(x => x.Code)
            .ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<AdmissionIntakeFormDto>>.SuccessResponse(rows.Select(MapForm).ToList());
    }

    public Task<ApiResponse<AdmissionIntakeFormDto>> CreateFormAsync(CreateAdmissionIntakeFormDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AdmissionIntakeFormDto>());
        if (request == null) return Task.FromResult(Error<AdmissionIntakeFormDto>("Admission form is required."));
        var validation = AdmissionIntakeRules.ValidateForm(request);
        if (request.ClientRequestId == Guid.Empty || validation != null)
            return Task.FromResult(Error<AdmissionIntakeFormDto>(validation ?? "Client request ID is required."));

        return ExecuteWriteAsync("create intake form", async () =>
        {
            var tenantId = _currentUser.TenantId;
            var normalized = Normalize(request);
            var replay = await _forms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.ClientRequestId == request.ClientRequestId, cancellationToken);
            if (replay != null)
            {
                if (!SameForm(replay, normalized)) return Error<AdmissionIntakeFormDto>("Client request ID was already used for a different admission form.", 409);
                return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(replay), "Admission form already exists.");
            }
            var referenceError = await ValidateReferencesAsync(normalized, cancellationToken);
            if (referenceError != null) return Error<AdmissionIntakeFormDto>(referenceError, 409);
            if (await _forms.GetQueryable().AnyAsync(x => x.TenantId == tenantId && x.Code == normalized.Code, cancellationToken))
                return Error<AdmissionIntakeFormDto>("Admission form code already exists.", 409);

            var now = _clock.GetUtcNow().UtcDateTime;
            var row = new AdmissionIntakeForm
            {
                TenantId = tenantId,
                PublicId = Guid.NewGuid(),
                ClientRequestId = request.ClientRequestId,
                Code = normalized.Code,
                Title = normalized.Title,
                Description = normalized.Description,
                AcademicYearId = normalized.AcademicYearId,
                AcademicTermId = normalized.AcademicTermId,
                CampusId = normalized.CampusId,
                AcademicUnitId = normalized.AcademicUnitId,
                OpensAtUtc = normalized.OpensAtUtc,
                ClosesAtUtc = normalized.ClosesAtUtc,
                ApplicationFee = normalized.ApplicationFee,
                Currency = normalized.Currency,
                FieldsJson = normalized.FieldsJson,
                DocumentRequirementsJson = normalized.RequirementsJson,
                Status = AdmissionIntakeFormStatus.Draft,
                CreatedAt = now,
                CreatedBy = _currentUser.UserId
            };
            await _forms.AddAsync(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return Created(MapForm(row), "Admission form created as draft.");
        });
    }

    public Task<ApiResponse<AdmissionIntakeFormDto>> UpdateFormAsync(long id, UpdateAdmissionIntakeFormDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AdmissionIntakeFormDto>());
        if (id <= 0 || request == null)
            return Task.FromResult(Error<AdmissionIntakeFormDto>("A valid form and row version are required."));
        var validation = AdmissionIntakeRules.ValidateForm(request);
        if (validation != null || !TryVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<AdmissionIntakeFormDto>(validation ?? "A valid form and row version are required."));

        return ExecuteWriteAsync("update intake form", async () =>
        {
            var row = await _forms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<AdmissionIntakeFormDto>("Admission form not found.", 404);
            if (row.Status != AdmissionIntakeFormStatus.Draft) return Error<AdmissionIntakeFormDto>("Only draft admission forms can be edited.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, version)) return Stale<AdmissionIntakeFormDto>();
            var normalized = Normalize(request);
            var referenceError = await ValidateReferencesAsync(normalized, cancellationToken);
            if (referenceError != null) return Error<AdmissionIntakeFormDto>(referenceError, 409);
            if (await _forms.GetQueryable().AnyAsync(x => x.TenantId == _currentUser.TenantId && x.Id != id && x.Code == normalized.Code, cancellationToken))
                return Error<AdmissionIntakeFormDto>("Admission form code already exists.", 409);

            row.Code = normalized.Code;
            row.Title = normalized.Title;
            row.Description = normalized.Description;
            row.AcademicYearId = normalized.AcademicYearId;
            row.AcademicTermId = normalized.AcademicTermId;
            row.CampusId = normalized.CampusId;
            row.AcademicUnitId = normalized.AcademicUnitId;
            row.OpensAtUtc = normalized.OpensAtUtc;
            row.ClosesAtUtc = normalized.ClosesAtUtc;
            row.ApplicationFee = normalized.ApplicationFee;
            row.Currency = normalized.Currency;
            row.FieldsJson = normalized.FieldsJson;
            row.DocumentRequirementsJson = normalized.RequirementsJson;
            Touch(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(row), "Admission form updated.");
        });
    }

    public Task<ApiResponse<AdmissionIntakeFormDto>> PublishFormAsync(long id, AdmissionRowVersionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AdmissionIntakeFormDto>());
        if (id <= 0 || request == null || !TryVersion(request.RowVersion, out var version)) return Task.FromResult(Error<AdmissionIntakeFormDto>("A valid form and row version are required."));
        return ExecuteWriteAsync("publish intake form", async () =>
        {
            var row = await _forms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<AdmissionIntakeFormDto>("Admission form not found.", 404);
            if (row.Status == AdmissionIntakeFormStatus.Published) return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(row), "Admission form is already published.");
            if (row.Status != AdmissionIntakeFormStatus.Draft) return Error<AdmissionIntakeFormDto>("Only draft admission forms can be published.", 409);
            if (row.ClosesAtUtc <= _clock.GetUtcNow().UtcDateTime) return Error<AdmissionIntakeFormDto>("Admission form closing time has passed.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, version)) return Stale<AdmissionIntakeFormDto>();
            row.Status = AdmissionIntakeFormStatus.Published;
            row.PublishedAtUtc = _clock.GetUtcNow().UtcDateTime;
            row.PublishedByUserId = _currentUser.UserId;
            Touch(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(row), "Admission form published.");
        });
    }

    public Task<ApiResponse<AdmissionIntakeFormDto>> CloseFormAsync(long id, AdmissionRowVersionDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AdmissionIntakeFormDto>());
        if (id <= 0 || request == null || !TryVersion(request.RowVersion, out var version)) return Task.FromResult(Error<AdmissionIntakeFormDto>("A valid form and row version are required."));
        return ExecuteWriteAsync("close intake form", async () =>
        {
            var row = await _forms.GetQueryable().FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == id, cancellationToken);
            if (row == null) return Error<AdmissionIntakeFormDto>("Admission form not found.", 404);
            if (row.Status == AdmissionIntakeFormStatus.Closed) return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(row), "Admission form is already closed.");
            if (row.Status != AdmissionIntakeFormStatus.Published) return Error<AdmissionIntakeFormDto>("Only published admission forms can be closed.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, version)) return Stale<AdmissionIntakeFormDto>();
            row.Status = AdmissionIntakeFormStatus.Closed;
            row.ClosedAtUtc = _clock.GetUtcNow().UtcDateTime;
            row.ClosedByUserId = _currentUser.UserId;
            Touch(row);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionIntakeFormDto>.SuccessResponse(MapForm(row), "Admission form closed.");
        });
    }

    public async Task<ApiResponse<IReadOnlyList<AdmissionApplicantDocumentDto>>> GetDocumentsAsync(Guid applicantReference, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<IReadOnlyList<AdmissionApplicantDocumentDto>>();
        var applicantId = await _applications.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.PublicId == applicantReference)
            .Select(x => (long?)x.Id).FirstOrDefaultAsync(cancellationToken);
        if (!applicantId.HasValue) return Error<IReadOnlyList<AdmissionApplicantDocumentDto>>("Application not found.", 404);
        var rows = await _documents.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _currentUser.TenantId && x.ApplicantId == applicantId.Value && x.IsCurrent)
            .OrderBy(x => x.DocumentType).ToListAsync(cancellationToken);
        return ApiResponse<IReadOnlyList<AdmissionApplicantDocumentDto>>.SuccessResponse(rows.Select(MapDocument).ToList());
    }

    public Task<ApiResponse<AdmissionApplicantDocumentDto>> ReviewDocumentAsync(Guid applicantReference, long documentId, ReviewAdmissionDocumentDto request, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Task.FromResult(Denied<AdmissionApplicantDocumentDto>());
        if (applicantReference == Guid.Empty || documentId <= 0 || request == null || request.Status is not (AdmissionDocumentVerificationStatus.Verified or AdmissionDocumentVerificationStatus.Rejected) || request.Note?.Length > 1000 || request.Status == AdmissionDocumentVerificationStatus.Rejected && string.IsNullOrWhiteSpace(request.Note) || !TryVersion(request.RowVersion, out var version))
            return Task.FromResult(Error<AdmissionApplicantDocumentDto>("A valid document decision, row version and rejection note are required."));

        return ExecuteWriteAsync("review applicant document", async () =>
        {
            var row = await _documents.GetQueryable().Include(x => x.Applicant)
                .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == documentId && x.IsCurrent && x.Applicant != null && x.Applicant.PublicId == applicantReference, cancellationToken);
            if (row == null) return Error<AdmissionApplicantDocumentDto>("Applicant document not found.", 404);
            var note = Trim(request.Note);
            if (row.VerificationStatus == request.Status)
            {
                if (row.ReviewNote != note) return Error<AdmissionApplicantDocumentDto>("Document was already reviewed with a different note.", 409);
                return ApiResponse<AdmissionApplicantDocumentDto>.SuccessResponse(MapDocument(row), "Document decision already saved.");
            }
            if (row.VerificationStatus != AdmissionDocumentVerificationStatus.Pending) return Error<AdmissionApplicantDocumentDto>("Only pending documents can be reviewed.", 409);
            if (!CryptographicOperations.FixedTimeEquals(row.RowVersion, version)) return Stale<AdmissionApplicantDocumentDto>();
            row.VerificationStatus = request.Status;
            row.ReviewNote = note;
            row.ReviewedAtUtc = _clock.GetUtcNow().UtcDateTime;
            row.ReviewedByUserId = _currentUser.UserId;
            row.UpdatedAt = row.ReviewedAtUtc;
            row.UpdatedBy = _currentUser.UserId;
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ApiResponse<AdmissionApplicantDocumentDto>.SuccessResponse(MapDocument(row), "Document decision saved.");
        });
    }

    public async Task<ApiResponse<AdmissionDocumentContentDto>> GetDocumentContentAsync(Guid applicantReference, long documentId, CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return Denied<AdmissionDocumentContentDto>();
        var row = await _documents.GetQueryable().AsNoTracking().Include(x => x.Applicant)
            .FirstOrDefaultAsync(x => x.TenantId == _currentUser.TenantId && x.Id == documentId && x.IsCurrent && x.Applicant != null && x.Applicant.PublicId == applicantReference, cancellationToken);
        if (row == null) return Error<AdmissionDocumentContentDto>("Applicant document not found.", 404);
        var file = await _storage.GetPrivateFileAsync(row.StorageKey);
        if (file == null) return Error<AdmissionDocumentContentDto>("Applicant document content is unavailable.", 404);
        return ApiResponse<AdmissionDocumentContentDto>.SuccessResponse(new AdmissionDocumentContentDto
        {
            Content = file.Content,
            ContentType = string.IsNullOrWhiteSpace(row.ContentType) ? file.ContentType : row.ContentType,
            FileName = row.OriginalFileName
        });
    }

    private async Task<string?> ValidateReferencesAsync(NormalizedForm input, CancellationToken cancellationToken)
    {
        var tenantId = _currentUser.TenantId;
        if (!await _years.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == input.AcademicYearId && x.IsActive, cancellationToken)) return "Academic year is unavailable.";
        if (input.AcademicTermId.HasValue && !await _terms.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == input.AcademicTermId && x.AcademicYearId == input.AcademicYearId && x.IsActive, cancellationToken)) return "Academic term does not belong to the selected year.";
        if (!await _campuses.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == input.CampusId && x.IsActive, cancellationToken)) return "Campus is unavailable.";
        if (!await _academicUnits.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == input.AcademicUnitId && x.IsActive, cancellationToken)) return "Academic unit is unavailable.";
        return null;
    }

    private Task<ApiResponse<T>> ExecuteWriteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action) => ExecuteAsync(operation, action);

    private async Task<ApiResponse<T>> ExecuteAsync<T>(string operation, Func<Task<ApiResponse<T>>> action)
    {
        try
        {
            var strategy = _unitOfWork.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                using var scope = new TransactionScope(TransactionScopeOption.Required, new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
                var response = await action();
                if (response.Success) scope.Complete();
                return response;
            });
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Stale admission intake write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Stale<T>();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Conflicting admission intake write during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Admission intake data conflicts with another update. Reload and try again.", 409);
        }
        catch (TransactionAbortedException ex)
        {
            _logger.LogWarning(ex, "Serialized admission intake write aborted during {Operation} for tenant {TenantId}", operation, _currentUser.TenantId);
            return Error<T>("Admission intake data conflicts with another update. Reload and try again.", 409);
        }
    }

    private void Touch(AdmissionIntakeForm row)
    {
        row.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
        row.UpdatedBy = _currentUser.UserId;
    }

    private static NormalizedForm Normalize(AdmissionIntakeFormInputDto x) => new(
        x.Code.Trim().ToUpperInvariant(), x.Title.Trim(), Trim(x.Description), x.AcademicYearId, x.AcademicTermId,
        x.CampusId, x.AcademicUnitId, AdmissionIntakeRules.Utc(x.OpensAtUtc), AdmissionIntakeRules.Utc(x.ClosesAtUtc),
        x.ApplicationFee, x.Currency.Trim().ToUpperInvariant(), AdmissionIntakeRules.SerializeFields(x.Fields), AdmissionIntakeRules.SerializeRequirements(x.DocumentRequirements));

    private static bool SameForm(AdmissionIntakeForm row, NormalizedForm x) =>
        row.Code == x.Code && row.Title == x.Title && row.Description == x.Description && row.AcademicYearId == x.AcademicYearId
        && row.AcademicTermId == x.AcademicTermId && row.CampusId == x.CampusId && row.AcademicUnitId == x.AcademicUnitId
        && row.OpensAtUtc == x.OpensAtUtc && row.ClosesAtUtc == x.ClosesAtUtc && row.ApplicationFee == x.ApplicationFee
        && row.Currency == x.Currency && row.FieldsJson == x.FieldsJson && row.DocumentRequirementsJson == x.RequirementsJson;

    private static AdmissionIntakeFormDto MapForm(AdmissionIntakeForm x) => new()
    {
        Id = x.Id, Reference = x.PublicId, Code = x.Code, Title = x.Title, Description = x.Description,
        AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId, CampusId = x.CampusId,
        AcademicUnitId = x.AcademicUnitId, OpensAtUtc = x.OpensAtUtc, ClosesAtUtc = x.ClosesAtUtc,
        ApplicationFee = x.ApplicationFee, Currency = x.Currency, Status = x.Status, PublishedAtUtc = x.PublishedAtUtc,
        ClosedAtUtc = x.ClosedAtUtc, Fields = AdmissionIntakeRules.ReadFields(x.FieldsJson),
        DocumentRequirements = AdmissionIntakeRules.ReadRequirements(x.DocumentRequirementsJson),
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    internal static AdmissionApplicantDocumentDto MapDocument(AdmissionApplicantDocument x) => new()
    {
        Id = x.Id, Reference = x.PublicId, DocumentType = x.DocumentType, OriginalFileName = x.OriginalFileName,
        ContentType = x.ContentType, FileSizeBytes = x.FileSizeBytes, VerificationStatus = x.VerificationStatus,
        IsCurrent = x.IsCurrent, UploadedAtUtc = x.UploadedAtUtc, ReviewedAtUtc = x.ReviewedAtUtc,
        ReviewNote = x.ReviewNote, RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    private bool CanManage() => _currentUser.IsAuthenticated && _currentUser.TenantId > 0 && (_currentUser.IsTenantAdmin || _currentUser.IsInRole("AdmissionOfficer"));
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrWhiteSpace(value)) return false;
        try { bytes = Convert.FromBase64String(value); return bytes.Length > 0; }
        catch (FormatException) { return false; }
    }

    private static ApiResponse<T> Created<T>(T data, string message) => new() { Success = true, Data = data, Message = message, StatusCode = 201 };
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static ApiResponse<T> Stale<T>() => Error<T>("Admission intake data changed. Reload and try again.", 409);
    private static ApiResponse<T> Denied<T>() => Error<T>("Admission officer access is required.", 403);

    private sealed record NormalizedForm(string Code, string Title, string? Description, long AcademicYearId, long? AcademicTermId,
        long CampusId, long AcademicUnitId, DateTime OpensAtUtc, DateTime ClosesAtUtc, decimal ApplicationFee, string Currency,
        string FieldsJson, string RequirementsJson);
}
