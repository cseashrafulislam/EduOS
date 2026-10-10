using EduOS.Core.Common;
using EduOS.Core.DTOs.Academic;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;

namespace EduOS.Service.Services.Tenants;

public sealed class InstitutionFoundationService : IInstitutionFoundationService
{
    private readonly IGenericRepository<Tenant> _tenants;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<TenantSetting> _settings;
    private readonly IGenericRepository<AcademicYear> _years;
    private readonly IGenericRepository<AcademicTerm> _terms;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<AdmissionIntakeForm> _admissions;
    private readonly IGenericRepository<TenantSubscription> _subscriptions;
    private readonly IGenericRepository<SubscriptionPlan> _plans;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUserService _user;
    private readonly TimeProvider _clock;
    private readonly ILogger<InstitutionFoundationService> _logger;

    public InstitutionFoundationService(IGenericRepository<Tenant> tenants,
        IGenericRepository<Campus> campuses, IGenericRepository<TenantSetting> settings,
        IGenericRepository<AcademicYear> years,
        IGenericRepository<AcademicTerm> terms, IGenericRepository<AcademicBatch> batches,
        IGenericRepository<AdmissionIntakeForm> admissions,
        IGenericRepository<TenantSubscription> subscriptions, IGenericRepository<SubscriptionPlan> plans,
        IUnitOfWork uow, ICurrentUserService user, TimeProvider clock,
        ILogger<InstitutionFoundationService> logger)
    {
        _tenants = tenants; _campuses = campuses; _settings = settings; _years = years; _terms = terms;
        _batches = batches; _admissions = admissions; _subscriptions = subscriptions;
        _plans = plans; _uow = uow; _user = user; _clock = clock; _logger = logger;
    }

    public async Task<ApiResponse<IReadOnlyList<CampusDto>>> GetCampusesAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<CampusDto>>("Tenant administrator required.", 403);
        var campuses = await _campuses.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted)
            .OrderByDescending(x => x.IsHeadOffice).ThenBy(x => x.Name)
            .Take(500).ToListAsync(ct);
        var keys = campuses.Select(x => CampusHeadKey(x.Id)).ToArray();
        var names = await _settings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && keys.Contains(x.Key) && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Key, x => x.Value, ct);
        IReadOnlyList<CampusDto> result = campuses.Select(x =>
            Map(x, names.GetValueOrDefault(CampusHeadKey(x.Id)))).ToList();
        return ApiResponse<IReadOnlyList<CampusDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<CampusDto>> GetCampusAsync(long id, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<CampusDto>("Tenant administrator required.", 403);
        var row = await _campuses.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<CampusDto>("Campus not found.", 404);
        var head = await GetHeadNameAsync(row.Id, ct);
        return ApiResponse<CampusDto>.SuccessResponse(Map(row, head));
    }

    public Task<ApiResponse<CampusDto>> SaveCampusAsync(long? id,
        SaveCampusRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<CampusDto>("Tenant administrator required.", 403));
        if (id is <= 0 || request == null || !ValidCode(request.Code, 50) ||
            !ValidName(request.Name, 150) || request.Email?.Length > 200 ||
            request.Phone?.Length > 30 || request.Address?.Length > 500 ||
            !request.IsActive)
            return Task.FromResult(Error<CampusDto>("Invalid or inactive campus; use Archive for removal."));
        return WriteAsync("save campus", async token =>
        {
            var tenantId = _user.TenantId;
            var tenant = await _tenants.GetQueryable().FirstOrDefaultAsync(x =>
                x.Id == tenantId && !x.IsDeleted && x.State != TenantState.Closed, token);
            if (tenant == null) return Error<CampusDto>("Institution not found.", 404);
            var row = id.HasValue ? await _campuses.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Id == id && !x.IsDeleted, token) : null;
            if (id.HasValue && row == null) return Error<CampusDto>("Campus not found.", 404);
            if (row != null && !Match(row.RowVersion, request.RowVersion))
                return Error<CampusDto>("Campus changed. Reload and retry.", 409);
            if (row != null && row.IsHeadOffice && !request.IsHeadOffice)
                return Error<CampusDto>("Select another head-office campus before removing this designation.", 409);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.Code == code && !x.IsDeleted &&
                (!id.HasValue || x.Id != id), token))
                return Error<CampusDto>("Campus code already exists.", 409);
            if (row == null)
            {
                var count = await _campuses.GetQueryable().AsNoTracking().CountAsync(x =>
                    x.TenantId == tenantId && x.IsActive && !x.IsDeleted, token);
                var now = _clock.GetUtcNow().UtcDateTime;
                var planId = await _subscriptions.GetQueryable().AsNoTracking().Where(x =>
                    x.TenantId == tenantId && !x.IsDeleted && x.StartsAt <= now &&
                    x.EndsAt > now &&
                    (x.State == SubscriptionState.Trial || x.State == SubscriptionState.Active))
                    .OrderByDescending(x => x.StartsAt).Select(x => (long?)x.SubscriptionPlanId)
                    .FirstOrDefaultAsync(token);
                var max = planId.HasValue ? await _plans.GetQueryable().AsNoTracking()
                    .Where(x => x.Id == planId.Value).Select(x => (int?)x.MaxCampuses)
                    .FirstOrDefaultAsync(token) ?? 1 : 1;
                if (max > 0 && count >= max) return Error<CampusDto>("Subscription campus limit reached.", 409);
                row = new Campus { TenantId = tenantId, PublicId = Guid.NewGuid(),
                    CreatedAt = now, CreatedBy = _user.UserId };
                await _campuses.AddAsync(row);
                request.IsHeadOffice = request.IsHeadOffice || count == 0;
            }
            var timestamp = _clock.GetUtcNow().UtcDateTime;
            if (request.IsHeadOffice)
            {
                var others = await _campuses.GetQueryable().Where(x =>
                    x.TenantId == tenantId && x.IsHeadOffice && !x.IsDeleted &&
                    (!id.HasValue || x.Id != id.Value)).ToListAsync(token);
                foreach (var other in others)
                {
                    other.IsHeadOffice = false;
                    other.UpdatedAt = timestamp; other.UpdatedBy = _user.UserId;
                    _campuses.Update(other);
                }
            }
            row.Name = request.Name.Trim(); row.Code = code;
            row.Phone = Trim(request.Phone); row.Email = Trim(request.Email);
            row.Address = Trim(request.Address);
            row.IsHeadOffice = request.IsHeadOffice; row.IsActive = request.IsActive;
            row.UpdatedAt = timestamp; row.UpdatedBy = _user.UserId;
            if (id.HasValue) _campuses.Update(row);
            TouchTenant(tenant, timestamp);
            await _uow.SaveChangesAsync(token);
            var headKey = CampusHeadKey(row.Id);
            var setting = await _settings.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenantId && x.Key == headKey && !x.IsDeleted, token);
            if (setting == null)
                await _settings.AddAsync(new TenantSetting
                {
                    TenantId = tenantId, Key = headKey, Category = "Campus",
                    Value = Trim(request.HeadName) ?? "", CreatedAt = timestamp, CreatedBy = _user.UserId
                });
            else
            {
                setting.Value = Trim(request.HeadName) ?? "";
                setting.UpdatedAt = timestamp; setting.UpdatedBy = _user.UserId;
                _settings.Update(setting);
            }
            await _uow.SaveChangesAsync(token);
            return ApiResponse<CampusDto>.SuccessResponse(Map(row, Trim(request.HeadName)), "Campus saved.");
        }, ct);
    }

    public Task<ApiResponse<bool>> ArchiveCampusAsync(long id, string version, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<bool>("Tenant administrator required.", 403));
        if (id <= 0) return Task.FromResult(Error<bool>("Campus not found.", 404));
        return WriteAsync("archive campus", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _campuses.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
            if (row == null) return Error<bool>("Campus not found.", 404);
            if (!Match(row.RowVersion, version)) return Error<bool>("Campus changed. Reload and retry.", 409);
            if (await _years.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.CampusId == id && !x.IsDeleted, token) ||
                await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.CampusId == id && !x.IsDeleted, token) ||
                await _admissions.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.CampusId == id && !x.IsDeleted, token))
                return Error<bool>("Campus has academic or admission records.", 409);
            var replacement = row.IsHeadOffice ? await _campuses.GetQueryable()
                .Where(x => x.TenantId == tenant && x.Id != id && x.IsActive && !x.IsDeleted)
                .OrderBy(x => x.Id).FirstOrDefaultAsync(token) : null;
            var now = _clock.GetUtcNow().UtcDateTime;
            if (replacement != null)
            {
                replacement.IsHeadOffice = true; replacement.UpdatedAt = now;
                replacement.UpdatedBy = _user.UserId; _campuses.Update(replacement);
            }
            row.IsActive = false; row.IsDeleted = true;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            _campuses.Update(row);
            var head = await _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == tenant, token);
            if (head != null) TouchTenant(head, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Campus archived.");
        }, ct);
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicYearDto>>> GetAcademicYearsAsync(CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<AcademicYearDto>>("Tenant administrator required.", 403);
        IReadOnlyList<AcademicYearDto> result = (await _years.GetQueryable().AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted)
            .OrderByDescending(x => x.StartDate).ThenByDescending(x => x.Id)
            .Take(300).ToListAsync(ct)).Select(Map).ToList();
        return ApiResponse<IReadOnlyList<AcademicYearDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<AcademicYearDto>> GetAcademicYearAsync(long id, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<AcademicYearDto>("Tenant administrator required.", 403);
        var row = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        return row == null ? Error<AcademicYearDto>("Academic year not found.", 404) :
            ApiResponse<AcademicYearDto>.SuccessResponse(Map(row));
    }

    public Task<ApiResponse<AcademicYearDto>> SaveAcademicYearAsync(long? id,
        SaveAcademicYearRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<AcademicYearDto>("Tenant administrator required.", 403));
        if (id is <= 0 || request == null || !ValidName(request.Name, 100) ||
            !ValidCode(request.Code, 30) || request.CampusId is <= 0 ||
            request.StartDate == default || request.EndDate <= request.StartDate ||
            !Enum.IsDefined(request.CycleType))
            return Task.FromResult(Error<AcademicYearDto>("Invalid academic year definition."));
        return WriteAsync("save academic year", async token =>
        {
            var tenant = _user.TenantId;
            if (request.CampusId.HasValue && !await _campuses.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Id == request.CampusId && x.IsActive && !x.IsDeleted, token))
                return Error<AcademicYearDto>("Campus not found.", 404);
            var row = id.HasValue ? await _years.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token) : null;
            if (id.HasValue && row == null) return Error<AcademicYearDto>("Academic year not found.", 404);
            if (row != null && !Match(row.RowVersion, request.RowVersion))
                return Error<AcademicYearDto>("Academic year changed. Reload and retry.", 409);
            var code = request.Code.Trim().ToUpperInvariant();
            if (await _years.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.Code == code && x.CampusId == request.CampusId &&
                !x.IsDeleted && (!id.HasValue || x.Id != id.Value), token))
                return Error<AcademicYearDto>("Academic year code already in use.", 409);
            if (row != null &&
                await _terms.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicYearId == row.Id && !x.IsDeleted &&
                    (x.StartDate < request.StartDate || x.EndDate > request.EndDate), token))
                return Error<AcademicYearDto>("Academic year cannot exclude existing terms.", 409);
            if (row != null && await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == row.Id && !x.IsDeleted &&
                (x.StartDate < request.StartDate || x.EndDate > request.EndDate), token))
                return Error<AcademicYearDto>("Academic year cannot exclude existing batches.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (request.IsCurrent)
            {
                var others = await _years.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.IsCurrent && x.CampusId == request.CampusId &&
                    !x.IsDeleted && (!id.HasValue || x.Id != id.Value)).ToListAsync(token);
                foreach (var existing in others)
                {
                    existing.IsCurrent = false; existing.UpdatedAt = now;
                    existing.UpdatedBy = _user.UserId; _years.Update(existing);
                }
            }
            if (row == null)
            {
                row = new AcademicYear { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                await _years.AddAsync(row);
            }
            row.Name = request.Name.Trim(); row.Code = code; row.CampusId = request.CampusId;
            row.CycleType = request.CycleType; row.StartDate = request.StartDate;
            row.EndDate = request.EndDate; row.IsCurrent = request.IsCurrent;
            row.IsActive = request.IsActive; row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            if (id.HasValue) _years.Update(row);
            var head = await _tenants.GetQueryable().FirstOrDefaultAsync(x => x.Id == tenant, token);
            if (head != null) TouchTenant(head, now);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AcademicYearDto>.SuccessResponse(Map(row), "Academic year saved.");
        }, ct);
    }

    public Task<ApiResponse<bool>> ArchiveAcademicYearAsync(long id, string version, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<bool>("Tenant administrator required.", 403));
        return WriteAsync("archive academic year", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _years.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
            if (row == null) return Error<bool>("Academic year not found.", 404);
            if (!Match(row.RowVersion, version))
                return Error<bool>("Academic year changed. Reload.", 409);
            if (await _terms.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == id && !x.IsDeleted, token) ||
                await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == id && !x.IsDeleted, token) ||
                await _admissions.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == id && !x.IsDeleted, token))
                return Error<bool>("Academic year has dependent records.", 409);
            row.IsActive = false; row.IsDeleted = true;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime; row.UpdatedBy = _user.UserId;
            _years.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Academic year archived.");
        }, ct);
    }

    public async Task<ApiResponse<IReadOnlyList<AcademicTermDto>>> GetAcademicTermsAsync(
        long? academicYearId = null, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<IReadOnlyList<AcademicTermDto>>("Tenant administrator required.", 403);
        if (academicYearId is <= 0) return Error<IReadOnlyList<AcademicTermDto>>("Invalid year ID.");
        var query = _terms.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && !x.IsDeleted);
        if (academicYearId.HasValue) query = query.Where(x => x.AcademicYearId == academicYearId.Value);
        var terms = await query.OrderByDescending(x => x.StartDate).ThenBy(x => x.Id)
            .Take(500).ToListAsync(ct);
        var yearIds = terms.Select(x => x.AcademicYearId).Distinct().ToArray();
        var names = await _years.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && yearIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        IReadOnlyList<AcademicTermDto> result = terms.Select(x => Map(x, names.GetValueOrDefault(x.AcademicYearId))).ToList();
        return ApiResponse<IReadOnlyList<AcademicTermDto>>.SuccessResponse(result);
    }

    public async Task<ApiResponse<AcademicTermDto>> GetAcademicTermAsync(long id, CancellationToken ct = default)
    {
        if (!CanManage()) return Error<AcademicTermDto>("Tenant administrator required.", 403);
        var row = await _terms.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == _user.TenantId && x.Id == id && !x.IsDeleted, ct);
        if (row == null) return Error<AcademicTermDto>("Academic term not found.", 404);
        var name = await _years.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.Id == row.AcademicYearId)
            .Select(x => x.Name).FirstOrDefaultAsync(ct);
        return ApiResponse<AcademicTermDto>.SuccessResponse(Map(row, name));
    }

    public Task<ApiResponse<AcademicTermDto>> SaveAcademicTermAsync(long? id,
        SaveAcademicTermRequestDto request, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<AcademicTermDto>("Tenant administrator required.", 403));
        if (id is <= 0 || request == null || request.AcademicYearId <= 0 ||
            !ValidName(request.Name, 100) || request.Code?.Length > 30 ||
            request.StartDate == default || request.EndDate <= request.StartDate || request.DisplayOrder < 0)
            return Task.FromResult(Error<AcademicTermDto>("Invalid academic term definition."));
        return WriteAsync("save academic term", async token =>
        {
            var tenant = _user.TenantId;
            var year = await _years.GetQueryable().AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == request.AcademicYearId &&
                x.IsActive && !x.IsDeleted, token);
            if (year == null) return Error<AcademicTermDto>("Academic year not found.", 404);
            if (request.StartDate < year.StartDate || request.EndDate > year.EndDate)
                return Error<AcademicTermDto>("Academic term exceeds academic year.", 409);
            var row = id.HasValue ? await _terms.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token) : null;
            if (id.HasValue && row == null) return Error<AcademicTermDto>("Academic term not found.", 404);
            if (row != null && !Match(row.RowVersion, request.RowVersion))
                return Error<AcademicTermDto>("Academic term changed. Reload.", 409);
            if (row != null && await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicTermId == row.Id && !x.IsDeleted &&
                (row.AcademicYearId != year.Id ||
                 x.StartDate < request.StartDate || x.EndDate > request.EndDate), token))
                return Error<AcademicTermDto>("Term cannot change beyond dates of existing batches or their year.", 409);
            if (row != null && row.AcademicYearId != year.Id &&
                await _admissions.GetQueryable().AsNoTracking().AnyAsync(x =>
                    x.TenantId == tenant && x.AcademicTermId == row.Id && !x.IsDeleted, token))
                return Error<AcademicTermDto>("Term with admission records cannot move to another year.", 409);
            if (await _terms.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicYearId == year.Id &&
                x.Name == request.Name.Trim() && !x.IsDeleted &&
                (!id.HasValue || x.Id != id.Value), token))
                return Error<AcademicTermDto>("Term name already exists for this year.", 409);
            var now = _clock.GetUtcNow().UtcDateTime;
            if (request.IsCurrent)
            {
                var others = await _terms.GetQueryable().Where(x =>
                    x.TenantId == tenant && x.AcademicYearId == year.Id &&
                    x.IsCurrent && !x.IsDeleted &&
                    (!id.HasValue || x.Id != id.Value)).ToListAsync(token);
                foreach (var other in others)
                {
                    other.IsCurrent = false; other.UpdatedAt = now;
                    other.UpdatedBy = _user.UserId; _terms.Update(other);
                }
            }
            if (row == null)
            {
                row = new AcademicTerm { TenantId = tenant, CreatedAt = now, CreatedBy = _user.UserId };
                await _terms.AddAsync(row);
            }
            row.AcademicYearId = year.Id; row.Name = request.Name.Trim();
            row.Code = Trim(request.Code); row.StartDate = request.StartDate;
            row.EndDate = request.EndDate; row.IsCurrent = request.IsCurrent;
            row.IsActive = request.IsActive; row.DisplayOrder = request.DisplayOrder;
            row.UpdatedAt = now; row.UpdatedBy = _user.UserId;
            if (id.HasValue) _terms.Update(row);
            await _uow.SaveChangesAsync(token);
            return ApiResponse<AcademicTermDto>.SuccessResponse(Map(row, year.Name), "Academic term saved.");
        }, ct);
    }

    public Task<ApiResponse<bool>> ArchiveAcademicTermAsync(long id, string version, CancellationToken ct = default)
    {
        if (!CanManage()) return Task.FromResult(Error<bool>("Tenant administrator required.", 403));
        return WriteAsync("archive term", async token =>
        {
            var tenant = _user.TenantId;
            var row = await _terms.GetQueryable().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == id && !x.IsDeleted, token);
            if (row == null) return Error<bool>("Academic term not found.", 404);
            if (!Match(row.RowVersion, version))
                return Error<bool>("Term changed. Reload.", 409);
            if (await _batches.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicTermId == id && !x.IsDeleted, token) ||
                await _admissions.GetQueryable().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenant && x.AcademicTermId == id && !x.IsDeleted, token))
                return Error<bool>("Academic term has dependent records.", 409);
            row.IsActive = false; row.IsDeleted = true;
            row.UpdatedAt = _clock.GetUtcNow().UtcDateTime; row.UpdatedBy = _user.UserId;
            _terms.Update(row); await _uow.SaveChangesAsync(token);
            return ApiResponse<bool>.SuccessResponse(true, "Academic term archived.");
        }, ct);
    }

    private static CampusDto Map(Campus row, string? headName = null) => new()
    {
        Id = row.Id, Reference = row.PublicId, Name = row.Name, Code = row.Code,
        Address = row.Address, Email = row.Email, Phone = row.Phone,
        HeadName = headName, IsHeadOffice = row.IsHeadOffice, IsActive = row.IsActive,
        RowVersion = Version(row.RowVersion)
    };
    private static string CampusHeadKey(long id) => "Campus." + id + ".HeadName";
    private Task<string?> GetHeadNameAsync(long id, CancellationToken ct) =>
        _settings.GetQueryable().AsNoTracking().Where(x =>
            x.TenantId == _user.TenantId && x.Key == CampusHeadKey(id) && !x.IsDeleted)
            .Select(x => x.Value).FirstOrDefaultAsync(ct);
    private static AcademicYearDto Map(AcademicYear row) => new()
    {
        Id = row.Id, Name = row.Name, Code = row.Code, CampusId = row.CampusId,
        StartDate = row.StartDate, EndDate = row.EndDate, CycleType = row.CycleType,
        IsCurrent = row.IsCurrent, IsActive = row.IsActive, RowVersion = Version(row.RowVersion)
    };
    private static AcademicTermDto Map(AcademicTerm row, string? yearName) => new()
    {
        Id = row.Id, AcademicYearId = row.AcademicYearId,
        AcademicYearName = yearName ?? string.Empty, Name = row.Name, Code = row.Code,
        StartDate = row.StartDate, EndDate = row.EndDate, IsCurrent = row.IsCurrent,
        IsActive = row.IsActive, DisplayOrder = row.DisplayOrder, RowVersion = Version(row.RowVersion)
    };
    private void TouchTenant(Tenant tenant, DateTime now)
    {
        tenant.UpdatedAt = now; tenant.UpdatedBy = _user.UserId; _tenants.Update(tenant);
    }
    private async Task<ApiResponse<T>> WriteAsync<T>(
        string operation, Func<CancellationToken, Task<ApiResponse<T>>> call, CancellationToken ct)
    {
        try { return await _uow.ExecuteInTransactionAsync(call, ct); }
        catch (DbUpdateConcurrencyException) { return Error<T>("Record changed concurrently.", 409); }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(ex, "Institution foundation setup conflict {Operation} tenant {TenantId}",
                operation, _user.TenantId);
            return Error<T>("Setup conflicts with an existing record.", 409);
        }
    }
    private bool CanManage() => _user.IsAuthenticated && _user.IsTenantAdmin && _user.TenantId > 0;
    private static bool ValidName(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static bool ValidCode(string? value, int max) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max;
    private static string? Trim(string? x) => string.IsNullOrWhiteSpace(x) ? null : x.Trim();
    private static string Version(byte[] version) => Convert.ToBase64String(version);
    private static bool Match(byte[] version, string? encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            var expected = Convert.FromBase64String(encoded);
            return version != null && version.Length > 0 && version.Length == expected.Length &&
                CryptographicOperations.FixedTimeEquals(version, expected);
        }
        catch (FormatException) { return false; }
    }
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
}
