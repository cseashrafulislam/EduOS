using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Transactions;

namespace EduOS.Service.Services.Admission;

public sealed class AdmissionApplicationService : IAdmissionApplicationService
{
    private readonly IGenericRepository<AdmissionApplicant> _applications;
    private readonly IGenericRepository<AdmissionIntakeForm> _forms;
    private readonly IGenericRepository<AdmissionFormField> _fields;
    private readonly IGenericRepository<AdmissionApplicantFieldValue> _values;
    private readonly IGenericRepository<AdmissionApplicantGuardian> _guardians;
    private readonly IGenericRepository<AdmissionApplicantDocument> _documents;
    private readonly IGenericRepository<AdmissionDecision> _decisions;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<AcademicLevel> _levels;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<AdmissionApplicationService> _logger;

    public AdmissionApplicationService(IGenericRepository<AdmissionApplicant> applications,
        IGenericRepository<AdmissionIntakeForm> forms, IGenericRepository<AdmissionFormField> fields,
        IGenericRepository<AdmissionApplicantFieldValue> values,
        IGenericRepository<AdmissionApplicantGuardian> guardians,
        IGenericRepository<AdmissionApplicantDocument> documents, IGenericRepository<AdmissionDecision> decisions,
        IGenericRepository<AcademicYear> years, IGenericRepository<AcademicTerm> terms,
        IGenericRepository<Campus> campuses, IGenericRepository<AcademicLevel> levels,
        IUnitOfWork unitOfWork, ICurrentUserService currentUser, TimeProvider clock,
        ILogger<AdmissionApplicationService> logger)
    {
        _applications = applications; _forms = forms; _fields = fields; _values = values;
        _guardians = guardians; _documents = documents; _decisions = decisions;
        _years = years; _terms = terms; _campuses = campuses; _levels = levels;
        _uow = unitOfWork; _user = currentUser; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<AdmissionApplicationOptionsDto>> GetOptionsAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationOptionsDto>();
        var tenant = _user.TenantId;
        var now = _clock.GetUtcNow().UtcDateTime;
        var years = await _years.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive)
            .OrderByDescending(x => x.StartDate)
            .Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).Take(100).ToListAsync(ct);
        var yearIds = years.Select(x => x.Id).ToArray();
        var terms = await _terms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive && yearIds.Contains(x.AcademicYearId))
            .OrderBy(x => x.DisplayOrder).Select(x => new AdmissionReferenceOptionDto
            { Id = x.Id, Name = x.Name, ParentId = x.AcademicYearId }).Take(200).ToListAsync(ct);
        var campuses = await _campuses.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive)
            .OrderBy(x => x.Name).Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).Take(200).ToListAsync(ct);
        var levels = await _levels.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant && x.IsActive)
            .OrderBy(x => x.LevelNo).Select(x => new AdmissionReferenceOptionDto { Id = x.Id, Name = x.Name }).Take(200).ToListAsync(ct);
        var formRows = await _forms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
            x.State == AdmissionFormState.Published && (!x.OpensAt.HasValue || x.OpensAt <= now) &&
            (!x.ClosesAt.HasValue || x.ClosesAt >= now)).OrderBy(x => x.Title).Take(100).ToListAsync(ct);
        var formIds = formRows.Select(x => x.Id).ToArray();
        var activeFields = await _fields.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && formIds.Contains(x.AdmissionIntakeFormId) && x.IsActive)
            .OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id).ToListAsync(ct);
        var fieldsByForm = activeFields.GroupBy(x => x.AdmissionIntakeFormId).ToDictionary(x => x.Key,
            x => x.Select(f => new AdmissionFormFieldDto
            {
                Id = f.Id, FieldKey = f.FieldKey, Label = f.Label, DataType = f.DataType,
                IsRequired = f.IsRequired, DisplayOrder = f.DisplayOrder, OptionsJson = f.OptionsJson,
                ValidationJson = f.ValidationJson, IsActive = f.IsActive
            }).ToList());
        var openForms = formRows.Select(x => new PublicAdmissionIntakeFormDto
        {
            Reference = x.PublicId, Code = x.Code, Title = x.Title, CampusId = x.CampusId,
            AcademicYearId = x.AcademicYearId, AcademicTermId = x.AcademicTermId,
            AcademicLevelId = x.AcademicLevelId, OpensAtUtc = x.OpensAt ?? DateTime.MinValue,
            ClosesAtUtc = x.ClosesAt ?? DateTime.MaxValue, ApplicationFee = x.ApplicationFee,
            Currency = x.CurrencyCode,
            Fields = fieldsByForm.GetValueOrDefault(x.Id) ?? new List<AdmissionFormFieldDto>()
        }).ToList();
        return ApiResponse<AdmissionApplicationOptionsDto>.SuccessResponse(new AdmissionApplicationOptionsDto
        { AcademicYears = years, AcademicTerms = terms, Campuses = campuses, AcademicLevels = levels, OpenForms = openForms });
    }

    public async Task<ApiResponse<PagedResult<AdmissionApplicationListItemDto>>> GetPageAsync(
        AdmissionApplicationQueryDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<PagedResult<AdmissionApplicationListItemDto>>();
        if (request == null || request.Page < 1 || request.PageSize is < 1 or > 100 ||
            request.Search?.Length > 100 || request.State.HasValue && !Enum.IsDefined(request.State.Value))
            return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Filter or pagination is invalid.");
        var tenant = _user.TenantId;
        var q = from app in _applications.GetQueryable().AsNoTracking()
                join form in _forms.GetQueryable().AsNoTracking() on app.AdmissionIntakeFormId equals form.Id
                join year in _years.GetQueryable().AsNoTracking() on form.AcademicYearId equals year.Id
                join campus in _campuses.GetQueryable().AsNoTracking() on form.CampusId equals campus.Id
                join level in _levels.GetQueryable().AsNoTracking() on form.AcademicLevelId equals level.Id
                where app.TenantId == tenant && form.TenantId == tenant && year.TenantId == tenant &&
                    campus.TenantId == tenant && level.TenantId == tenant
                select new { app, form, year, campus, level };
        var term = request.Search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
            q = q.Where(x => x.app.ApplicationNumber.Contains(term) || x.app.FullName.Contains(term));
        if (request.AcademicYearId.HasValue) q = q.Where(x => x.form.AcademicYearId == request.AcademicYearId.Value);
        if (request.CampusId.HasValue) q = q.Where(x => x.form.CampusId == request.CampusId.Value);
        if (request.AcademicLevelId.HasValue) q = q.Where(x => x.form.AcademicLevelId == request.AcademicLevelId.Value);
        if (request.State.HasValue)
        {
            var state = request.State.Value;
            if (!Enum.IsDefined(state)) return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Unsupported application status filter.");
            q = q.Where(x => x.app.State == state);
        }
        var total = await q.CountAsync(ct);
        var skip = ((long)request.Page - 1) * request.PageSize;
        if (skip > int.MaxValue) return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.ErrorResponse("Page is outside the result range.");
        var data = await q.OrderByDescending(x => x.app.SubmittedAt).ThenByDescending(x => x.app.Id)
            .Skip((int)skip).Take(request.PageSize).Select(x => new
            {
                x.app.PublicId, x.app.ApplicationNumber, x.app.FullName, x.app.Phone, x.app.State,
                x.app.SubmittedAt, x.app.RowVersion, YearId = x.year.Id, YearName = x.year.Name,
                CampusId = x.campus.Id, CampusName = x.campus.Name,
                LevelId = x.level.Id, LevelName = x.level.Name
            }).ToListAsync(ct);
        var rows = data.Select(x => new AdmissionApplicationListItemDto
        {
            Reference = x.PublicId, ApplicationNumber = x.ApplicationNumber,
            ApplicantName = x.FullName, MaskedMobile = Mask(x.Phone),
            AcademicYearId = x.YearId, AcademicYearName = x.YearName,
            CampusId = x.CampusId, CampusName = x.CampusName,
            AcademicLevelId = x.LevelId, AcademicLevelName = x.LevelName,
            State = x.State, SubmittedAtUtc = x.SubmittedAt ?? DateTime.MinValue,
            RowVersion = Convert.ToBase64String(x.RowVersion)
        }).ToList();
        return ApiResponse<PagedResult<AdmissionApplicationListItemDto>>.SuccessResponse(new PagedResult<AdmissionApplicationListItemDto>
        { Items = rows, TotalCount = total, Page = request.Page, PageSize = request.PageSize });
    }

    public async Task<ApiResponse<AdmissionApplicationDetailsDto>> GetByReferenceAsync(Guid reference, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationDetailsDto>();
        if (reference == Guid.Empty) return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Applicant reference is invalid.");
        var dto = await ReadDetailsAsync(reference, ct);
        return dto == null ? ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Applicant not found.", 404) :
            ApiResponse<AdmissionApplicationDetailsDto>.SuccessResponse(dto);
    }

    public async Task<ApiResponse<AdmissionApplicationCreatedDto>> CreateAsync(CreateAdmissionApplicationDto request,
        CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationCreatedDto>();
        if (request == null || request.ClientRequestId == Guid.Empty || request.AdmissionFormReference is null ||
            string.IsNullOrWhiteSpace(request.ApplicantName) || request.ApplicantName.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(request.PrimaryMobile) || request.PrimaryMobile.Trim().Length > 30 ||
            request.DateOfBirth.Date < new DateTime(1900, 1, 1) ||
            request.DateOfBirth.Date > _clock.GetUtcNow().UtcDateTime.Date ||
            !Enum.IsDefined(request.Gender))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("A valid applicant and configured intake form are required.");
        if (request.PreviousInstitution != null && !string.IsNullOrWhiteSpace(request.PreviousInstitution))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Previous institution is not supported by the canonical applicant record.");
        if (!string.IsNullOrWhiteSpace(request.PermanentAddress) && !string.IsNullOrWhiteSpace(request.PresentAddress) &&
            !string.Equals(request.PermanentAddress.Trim(), request.PresentAddress.Trim(), StringComparison.Ordinal))
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Distinct permanent and present addresses require a separate address workflow.");
        var tenant = _user.TenantId;
        try
        {
            using var tx = new TransactionScope(TransactionScopeOption.Required,
                new TransactionOptions { IsolationLevel = IsolationLevel.Serializable }, TransactionScopeAsyncFlowOption.Enabled);
            var form = await _forms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == request.AdmissionFormReference &&
                x.State == AdmissionFormState.Published, ct);
            if (form == null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Published intake form not found.", 404);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (form.OpensAt > now || form.ClosesAt < now)
                return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Intake form is not open.", 409);
            if (form.AcademicYearId != request.AcademicYearId || form.CampusId != request.CampusId ||
                form.AcademicLevelId != request.AcademicLevelId || form.AcademicTermId != request.AcademicTermId)
                return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Academic choices do not match the intake form.", 409);
            var fields = await _fields.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.AdmissionIntakeFormId == form.Id && x.IsActive).OrderBy(x => x.DisplayOrder)
                .Select(x => new AdmissionFormFieldDto
                { Id = x.Id, FieldKey = x.FieldKey, Label = x.Label, DataType = x.DataType,
                    IsRequired = x.IsRequired, OptionsJson = x.OptionsJson, ValidationJson = x.ValidationJson,
                    IsActive = x.IsActive }).ToListAsync(ct);
            var validation = AdmissionIntakeRules.ValidateResponses(fields, request.CustomResponses, out _);
            if (validation != null) return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse(validation);
            var existing = await _applications.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId, ct);
            if (existing != null)
            {
                if (existing.AdmissionIntakeFormId != form.Id ||
                    !string.Equals(existing.FullName, request.ApplicantName.Trim(), StringComparison.Ordinal) ||
                    existing.Phone != request.PrimaryMobile.Trim())
                    return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Request ID was previously used for another application.", 409);
                tx.Complete();
                return ApiResponse<AdmissionApplicationCreatedDto>.SuccessResponse(MapCreated(existing), "Application already submitted.");
            }
            var isMinor = request.DateOfBirth.Date > now.Date.AddYears(-18);
            if (isMinor && (string.IsNullOrWhiteSpace(request.GuardianName) ||
                string.IsNullOrWhiteSpace(request.GuardianRelation) || string.IsNullOrWhiteSpace(request.GuardianMobile)))
                return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Guardian details are required for a minor.");
            var id = Guid.NewGuid();
            var applicant = new AdmissionApplicant
            {
                TenantId = tenant, PublicId = id, ClientRequestId = request.ClientRequestId,
                AdmissionIntakeFormId = form.Id,
                ApplicationNumber = ("APP-" + now.ToString("yyyy") + "-" + id.ToString("N")).Substring(0, 17).ToUpperInvariant(),
                FullName = request.ApplicantName.Trim(), FullNameBangla = Trim(request.ApplicantNameBangla),
                DateOfBirth = DateOnly.FromDateTime(request.DateOfBirth.Date), Gender = request.Gender.ToString(),
                Phone = request.PrimaryMobile.Trim(), Email = Trim(request.Email),
                Address = Trim(request.PermanentAddress) ?? Trim(request.PresentAddress),
                State = AdmissionApplicantState.Submitted, SubmittedAt = now,
                CreatedAt = now, CreatedBy = _user.UserId
            };
            await _applications.AddAsync(applicant);
            await _uow.SaveChangesAsync(ct);
            foreach (var f in fields)
            {
                var value = request.CustomResponses != null && request.CustomResponses.TryGetValue(f.FieldKey, out var supplied)
                    ? supplied : null;
                if (string.IsNullOrWhiteSpace(value)) continue;
                await _values.AddAsync(new AdmissionApplicantFieldValue
                {
                    TenantId = tenant, AdmissionApplicantId = applicant.Id, AdmissionFormFieldId = f.Id,
                    Value = value, CreatedAt = now, CreatedBy = _user.UserId
                });
            }
            if (!string.IsNullOrWhiteSpace(request.GuardianName))
                await _guardians.AddAsync(new AdmissionApplicantGuardian
                {
                    TenantId = tenant, AdmissionApplicantId = applicant.Id, FullName = request.GuardianName.Trim(),
                    RelationCode = request.GuardianRelation?.Trim() ?? "Guardian",
                    Phone = Trim(request.GuardianMobile), IsPrimary = true, CreatedAt = now, CreatedBy = _user.UserId
                });
            await _uow.SaveChangesAsync(ct);
            tx.Complete();
            return new ApiResponse<AdmissionApplicationCreatedDto>
            { Success = true, StatusCode = 201, Message = "Application submitted.", Data = MapCreated(applicant) };
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Intake changed during submission.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Duplicate or invalid applicant data for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Application conflicts with an existing request.", 409);
        }
        catch (TransactionAbortedException)
        { return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Concurrent application submission detected.", 409); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admission application failed for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionApplicationCreatedDto>.ErrorResponse("Application could not be submitted.", 500);
        }
    }

    public async Task<ApiResponse<AdmissionApplicationDetailsDto>> ReviewAsync(Guid reference,
        ReviewAdmissionApplicationDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Denied<AdmissionApplicationDetailsDto>();
        if (reference == Guid.Empty || request == null ||
            !TryVersion(request.RowVersion, out var expected))
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Valid applicant reference and row version are required.");
        if (request.State is not (AdmissionApplicantState.UnderReview or AdmissionApplicantState.Qualified or
            AdmissionApplicantState.Rejected or AdmissionApplicantState.Withdrawn))
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Unsupported review status. Waitlisting and admission require their dedicated workflows.");
        var tenant = _user.TenantId;
        try
        {
            var app = await _applications.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.PublicId == reference, ct);
            if (app == null) return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Applicant not found.", 404);
            if (!VersionsMatch(app.RowVersion, expected))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application changed. Reload and retry.", 409);
            if (app.State is AdmissionApplicantState.Admitted or AdmissionApplicantState.Rejected or AdmissionApplicantState.Withdrawn)
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("A final decision cannot be silently changed.", 409);
            if (request.State == AdmissionApplicantState.UnderReview &&
                app.State is not (AdmissionApplicantState.Submitted or AdmissionApplicantState.DocumentPending))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application cannot move to review.", 409);
            if (request.State == AdmissionApplicantState.Qualified &&
                app.State is not (AdmissionApplicantState.Submitted or AdmissionApplicantState.UnderReview or AdmissionApplicantState.AssessmentPending))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application cannot be qualified from its current state.", 409);
            if (request.State is AdmissionApplicantState.Rejected or AdmissionApplicantState.Withdrawn &&
                string.IsNullOrWhiteSpace(request.DecisionNote))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("A reason is required for rejection or withdrawal.");
            if (request.DecisionNote?.Length > 1000)
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Decision note is too long.");
            if (request.State == AdmissionApplicantState.Withdrawn)
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Applicant withdrawal requires a verified applicant request.", 403);
            if (request.State == AdmissionApplicantState.Qualified &&
                await _documents.GetQueryable().AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.AdmissionApplicantId == app.Id && !x.IsVerified, ct))
                return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Unverified documents must be reviewed before qualification.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            app.State = request.State;
            app.ReviewedAt = now; app.ReviewedByUserId = _user.UserId;
            app.UpdatedAt = now; app.UpdatedBy = _user.UserId;
            if (request.State == AdmissionApplicantState.Rejected)
                await _decisions.AddAsync(new AdmissionDecision
                {
                    TenantId = tenant, ClientRequestId = Guid.NewGuid(), AdmissionApplicantId = app.Id,
                    State = AdmissionDecisionState.Rejected, DecidedByUserId = _user.UserId,
                    Note = Trim(request.DecisionNote), CreatedAt = now, CreatedBy = _user.UserId
                });
            await _uow.SaveChangesAsync(ct);
            var dto = await ReadDetailsAsync(reference, ct);
            return dto == null ? ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Applicant could not be reloaded.", 500) :
                ApiResponse<AdmissionApplicationDetailsDto>.SuccessResponse(dto, "Application review saved.");
        }
        catch (DbUpdateConcurrencyException)
        { return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Application changed. Reload and retry.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Admission review conflict for tenant {TenantId}", tenant);
            return ApiResponse<AdmissionApplicationDetailsDto>.ErrorResponse("Review conflicts with another decision.", 409);
        }
    }

    private async Task<AdmissionApplicationDetailsDto?> ReadDetailsAsync(Guid reference, CancellationToken ct)
    {
        var tenant = _user.TenantId;
        var data = await (from applicant in _applications.GetQueryable().AsNoTracking()
            join form in _forms.GetQueryable().AsNoTracking() on applicant.AdmissionIntakeFormId equals form.Id
            join year in _years.GetQueryable().AsNoTracking() on form.AcademicYearId equals year.Id
            join campus in _campuses.GetQueryable().AsNoTracking() on form.CampusId equals campus.Id
            join level in _levels.GetQueryable().AsNoTracking() on form.AcademicLevelId equals level.Id
            where applicant.TenantId == tenant && form.TenantId == tenant && year.TenantId == tenant &&
                campus.TenantId == tenant && level.TenantId == tenant && applicant.PublicId == reference
            select new { applicant, form, year, campus, level }).FirstOrDefaultAsync(ct);
        if (data == null) return null;
        var a = data.applicant;
        var termName = data.form.AcademicTermId.HasValue
            ? await _terms.GetQueryable().AsNoTracking().Where(x => x.TenantId == tenant &&
                x.Id == data.form.AcademicTermId.Value).Select(x => x.Name).FirstOrDefaultAsync(ct) : null;
        var guardians = await _guardians.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AdmissionApplicantId == a.Id).OrderByDescending(x => x.IsPrimary)
            .ToListAsync(ct);
        var fieldRows = await (from value in _values.GetQueryable().AsNoTracking()
            join field in _fields.GetQueryable().AsNoTracking() on value.AdmissionFormFieldId equals field.Id
            where value.TenantId == tenant && field.TenantId == tenant && value.AdmissionApplicantId == a.Id
            select new { field.FieldKey, value.Value }).ToListAsync(ct);
        var decisionNote = await _decisions.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == tenant && x.AdmissionApplicantId == a.Id && x.State == AdmissionDecisionState.Rejected)
            .OrderByDescending(x => x.Id).Select(x => x.Note).FirstOrDefaultAsync(ct);
        var firstGuardian = guardians.FirstOrDefault();
        var dt = new AdmissionApplicationDetailsDto
        {
            Reference = a.PublicId, ApplicationNumber = a.ApplicationNumber, ApplicantName = a.FullName,
            ApplicantNameBangla = a.FullNameBangla, MaskedMobile = Mask(a.Phone), PrimaryMobile = a.Phone ?? "",
            State = a.State, SubmittedAtUtc = a.SubmittedAt ?? DateTime.MinValue,
            RowVersion = Convert.ToBase64String(a.RowVersion),
            AcademicYearId = data.year.Id, AcademicYearName = data.year.Name,
            CampusId = data.campus.Id, CampusName = data.campus.Name,
            AcademicLevelId = data.level.Id, AcademicLevelName = data.level.Name,
            AcademicTermId = data.form.AcademicTermId, AcademicTermName = termName,
            AdmissionFormReference = data.form.PublicId, AdmissionFormTitle = data.form.Title,
            CustomResponses = fieldRows.ToDictionary(x => x.FieldKey, x => x.Value),
            DateOfBirth = a.DateOfBirth?.ToDateTime(TimeOnly.MinValue) ?? default,
            Gender = Enum.TryParse<Gender>(a.Gender, true, out var g) ? g : default,
            Email = a.Email, GuardianName = firstGuardian?.FullName,
            GuardianRelation = firstGuardian?.RelationCode, GuardianMobile = firstGuardian?.Phone,
            PresentAddress = a.Address, PermanentAddress = a.Address,
            PreferredLanguage = "bn-BD", ReviewedAtUtc = a.ReviewedAt, DecisionNote = decisionNote
        };
        return dt;
    }

    private static AdmissionApplicationCreatedDto MapCreated(AdmissionApplicant app) => new()
    {
        Reference = app.PublicId, ApplicationNumber = app.ApplicationNumber,
        State = app.State, RowVersion = Convert.ToBase64String(app.RowVersion)
    };
    private static string Mask(string? phone) => string.IsNullOrWhiteSpace(phone) ? string.Empty :
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool TryVersion(string? input, out byte[] version)
    {
        version = Array.Empty<byte>();
        try
        {
            version = Convert.FromBase64String(input ?? "");
            return version.Length > 0;
        }
        catch (FormatException) { return false; }
    }
    private static bool VersionsMatch(byte[] a, byte[] b) =>
        a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 &&
        (_user.IsTenantAdmin || _user.IsInRole("AdmissionOfficer"));
    private static ApiResponse<T> Denied<T>() =>
        ApiResponse<T>.ErrorResponse("Admission officer access is required.", 403);
}
