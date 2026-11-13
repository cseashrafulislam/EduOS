using EduOS.Core.Common;
using EduOS.Core.DTOs.Files;
using EduOS.Core.DTOs.LMS;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Helpers.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Security.Cryptography;

namespace EduOS.Service.Services.LMS;

/// <summary>
/// Teacher content moderation on one tenant-owned resource/version aggregate.
/// FileAsset remains the canonical scanned private blob, LessonPlan the canonical
/// teaching plan, and Course/LessonResource the canonical learner delivery path.
/// No resource is shared with another institution by a caller-supplied scope.
/// </summary>
public sealed class TeacherResourceService(EduOSDbContext db, ICurrentUserService user,
    IDocumentFileService files, IFileUploadService storage, TimeProvider clock,
    ILogger<TeacherResourceService> logger) : ITeacherResourceService
{
    private long Tenant => user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private bool Auth => user.IsAuthenticated && user.UserId > 0 && Tenant > 0 &&
        db.CurrentTenantId == Tenant;
    private bool Manager => Auth && user.IsTenantAdmin;
    private bool Teacher => Auth && user.IsInRole("Teacher");
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);

    private IQueryable<long> MyActiveEmployees() => db.Employees.AsNoTracking()
        .Where(x => x.TenantId == Tenant && x.UserId == user.UserId &&
            x.State == EmployeeState.Active && x.CanTeach && !x.IsDeleted).Select(x => x.Id);

    private IQueryable<long> MyCampuses() => db.EmployeeCampusAssignments.AsNoTracking()
        .Where(x => x.TenantId == Tenant && MyActiveEmployees().Contains(x.EmployeeId) &&
            x.IsCurrent && !x.IsDeleted && x.EffectiveFrom <= DateOnly.FromDateTime(Now) &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo >= DateOnly.FromDateTime(Now)))
        .Select(x => x.CampusId);

    private IQueryable<TeacherResource> Visible()
    {
        var query = db.TeacherResources.AsNoTracking().Where(x =>
            x.TenantId == Tenant && !x.IsDeleted);
        if (Manager) return query;
        var employees = MyActiveEmployees();
        var campuses = MyCampuses();
        return query.Where(x => employees.Contains(x.AuthorEmployeeId) ||
            (x.StateCode == "Approved" &&
                (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc > Now) &&
                ((x.ScopeCode == "Institution" || x.ScopeCode == "Public") && campuses.Any() ||
                 x.ScopeCode == "Campus" && campuses.Contains(x.CampusId))));
    }

    private static bool Version(byte[] current, string? encoded)
    {
        if (current.Length == 0 || string.IsNullOrWhiteSpace(encoded)) return false;
        try
        {
            var expected = Convert.FromBase64String(encoded);
            return expected.Length == current.Length &&
                CryptographicOperations.FixedTimeEquals(current, expected);
        }
        catch (FormatException) { return false; }
    }

    private static bool Https(string? value) => string.IsNullOrWhiteSpace(value) ||
        (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
            !uri.IsLoopback && string.IsNullOrEmpty(uri.UserInfo) &&
            !uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase));

    private static bool Valid(TeacherResourceUploadRequestDto? x) => x != null &&
        x.RequestId != Guid.Empty && x.CampusId > 0 &&
        !string.IsNullOrWhiteSpace(x.Title) && x.Title.Trim().Length <= 250 &&
        (x.KindCode is "LessonPlan" or "Slide" or "Worksheet" or "Assessment" or "Exemplar" or "Other") &&
        (x.ScopeCode is "Private" or "Campus" or "Institution" or "Public") &&
        (x.LanguageCode is "bn-BD" or "en-BD") &&
        (x.Topic == null || x.Topic.Length <= 150) &&
        (x.SourceUrl == null || x.SourceUrl.Length <= 1000) && Https(x.SourceUrl) &&
        !string.IsNullOrWhiteSpace(x.RightsHolder) && x.RightsHolder.Length <= 200 &&
        (x.LicenceCode is "InstitutionOwned" or "PermissionGranted" or "CreativeCommons" or "PublicDomain") &&
        !string.IsNullOrWhiteSpace(x.LicenceEvidence) && x.LicenceEvidence.Length <= 500 &&
        (x.ScopeCode != "Public" ||
            (x.LicenceCode is "CreativeCommons" or "PublicDomain") &&
            !string.IsNullOrWhiteSpace(x.SourceUrl) && Https(x.SourceUrl)) &&
        (!x.ExpiresAtUtc.HasValue || x.ExpiresAtUtc.Value.Kind == DateTimeKind.Utc);

    private static TeacherResourceDto Map(TeacherResource row, FileAsset asset,
        Guid? plan = null, Guid? predecessor = null) => new()
    {
        Reference = row.PublicId, CampusId = row.CampusId,
        AuthorEmployeeId = row.AuthorEmployeeId, AcademicLevelId = row.AcademicLevelId,
        SubjectId = row.SubjectId, LessonPlanReference = plan,
        SupersedesReference = predecessor, Title = row.Title, KindCode = row.KindCode,
        ScopeCode = row.ScopeCode, StateCode = row.StateCode, Topic = row.Topic,
        LanguageCode = row.LanguageCode, SourceUrl = row.SourceUrl,
        RightsHolder = row.RightsHolder, LicenceCode = row.LicenceCode,
        LicenceEvidence = row.LicenceEvidence, FileName = asset.OriginalFileName,
        FileBytes = asset.SizeBytes, VersionNo = row.VersionNo,
        ReviewedByUserId = row.ReviewedByUserId, ReviewedAtUtc = row.ReviewedAtUtc,
        ReviewNote = row.ReviewNote, PublishedAtUtc = row.PublishedAtUtc,
        ExpiresAtUtc = row.ExpiresAtUtc,
        RowVersion = Convert.ToBase64String(row.RowVersion)
    };

    private AuditLog Audit(TeacherResource row, string action) => new()
    {
        TenantId = Tenant, UserId = user.UserId, Action = action,
        EntityName = nameof(TeacherResource), EntityId = row.Id,
        CorrelationId = row.PublicId.ToString("N"), NewValue = row.StateCode,
        IsSuccess = true, OccurredAt = Now
    };

    private async Task<TeacherResourceDto> MapAsync(TeacherResource row, CancellationToken ct)
    {
        var asset = await db.FileAssets.AsNoTracking().SingleAsync(x =>
            x.TenantId == Tenant && x.Id == row.FileAssetId, ct);
        var plan = row.LessonPlanId.HasValue ? await db.LessonPlans.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.Id == row.LessonPlanId.Value)
            .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
        var previous = row.SupersedesResourceId.HasValue ?
            await db.TeacherResources.AsNoTracking().Where(x =>
                x.TenantId == Tenant && x.Id == row.SupersedesResourceId.Value)
                .Select(x => (Guid?)x.PublicId).FirstOrDefaultAsync(ct) : null;
        var mapped = Map(row, asset, plan, previous);
        mapped.CanModify = Teacher && await MyActiveEmployees().ContainsAsync(row.AuthorEmployeeId, ct);
        return mapped;
    }

    public async Task<ApiResponse<TeacherStudioOptionsDto>> OptionsAsync(CancellationToken ct = default)
    {
        if (!Manager && !Teacher) return Error<TeacherStudioOptionsDto>("Teacher access required.", 403);
        var accessible = Manager
            ? db.Campuses.AsNoTracking().Where(x => x.TenantId == Tenant && x.IsActive && !x.IsDeleted)
                .Select(x => x.Id)
            : MyCampuses();
        var campuses = await db.Campuses.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.IsActive && !x.IsDeleted && accessible.Contains(x.Id))
            .OrderBy(x => x.Name).Take(100)
            .Select(x => new TeacherStudioOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var subjects = await db.Subjects.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name).Take(200)
            .Select(x => new TeacherStudioOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var levels = await db.AcademicLevels.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.Name).Take(200)
            .Select(x => new TeacherStudioOptionDto { Id = x.Id, Name = x.Name }).ToListAsync(ct);
        var employees = MyActiveEmployees();
        var plans = await db.LessonPlans.AsNoTracking().Where(x =>
            x.TenantId == Tenant && !x.IsDeleted && employees.Contains(x.EmployeeId))
            .OrderByDescending(x => x.LessonDate).ThenByDescending(x => x.Id).Take(100)
            .Select(x => new TeacherStudioOptionDto { Id = x.Id, Reference = x.PublicId,
                Name = x.Title }).ToListAsync(ct);
        return ApiResponse<TeacherStudioOptionsDto>.SuccessResponse(new TeacherStudioOptionsDto
        { Campuses = campuses, Subjects = subjects, Levels = levels, LessonPlans = plans });
    }

    public async Task<ApiResponse<PagedResult<TeacherResourceDto>>> ListAsync(
        string? search, string? state, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager && !Teacher) return Error<PagedResult<TeacherResourceDto>>("Teacher access required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize ||
            search?.Length > 100 || state is not (null or "" or "Draft" or "Submitted" or "Approved" or "Rejected" or "Withdrawn"))
            return Error<PagedResult<TeacherResourceDto>>("Invalid search or page filter.");
        var query = Visible();
        if (!string.IsNullOrWhiteSpace(state)) query = query.Where(x => x.StateCode == state);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.Title.Contains(term) || (x.Topic != null && x.Topic.Contains(term)));
        }
        var total = await query.CountAsync(ct);
        var rows = await (from resource in query
            join file in db.FileAssets.AsNoTracking() on new { resource.TenantId, Id = resource.FileAssetId }
                equals new { file.TenantId, file.Id }
            where !file.IsDeleted
            orderby resource.CreatedAt descending, resource.Id descending
            select new { Resource = resource, File = file })
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var authorIds = Teacher ? await MyActiveEmployees().ToListAsync(ct) : new List<long>();
        return ApiResponse<PagedResult<TeacherResourceDto>>.SuccessResponse(new PagedResult<TeacherResourceDto>
        {
            Items = rows.Select(x =>
            {
                var mapped = Map(x.Resource, x.File);
                mapped.CanModify = authorIds.Contains(x.Resource.AuthorEmployeeId);
                return mapped;
            }).ToList(),
            TotalCount = total, Page = page, PageSize = pageSize
        });
    }

    public async Task<ApiResponse<PagedResult<TeacherPublicResourceDto>>> ListPublicAsync(
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Manager && !Teacher)
            return Error<PagedResult<TeacherPublicResourceDto>>("Teacher access required.", 403);
        if (!Manager && !await MyActiveEmployees().AnyAsync(ct))
            return Error<PagedResult<TeacherPublicResourceDto>>("Active teacher required.", 403);
        if (page < 1 || pageSize is < 1 or > 50 || page > int.MaxValue / pageSize ||
            search?.Length > 100)
            return Error<PagedResult<TeacherPublicResourceDto>>("Invalid public catalogue filter.");
        // The only intentionally cross-tenant read in Teacher Studio. No private
        // FileAsset content, evidence, employee details or source tenant IDs leave
        // the server. Source tenant suspension and asset revocation take effect live.
        var query = from resource in db.TeacherResources.IgnoreQueryFilters().AsNoTracking()
                    join institution in db.Tenants.AsNoTracking() on resource.TenantId equals institution.Id
                    join file in db.FileAssets.IgnoreQueryFilters().AsNoTracking()
                        on new { resource.TenantId, Id = resource.FileAssetId }
                        equals new { file.TenantId, file.Id }
                    where !resource.IsDeleted && resource.ScopeCode == "Public" &&
                        resource.StateCode == "Approved" && resource.PublishedAtUtc.HasValue &&
                        (!resource.ExpiresAtUtc.HasValue || resource.ExpiresAtUtc > Now) &&
                        (resource.LicenceCode == "CreativeCommons" || resource.LicenceCode == "PublicDomain") &&
                        resource.SourceUrl != null && resource.SourceUrl.StartsWith("https://") &&
                        !institution.IsDeleted && institution.State == TenantState.Active &&
                        !file.IsDeleted && file.IsVerifiedSafe && file.MalwareScanStateCode == "Clean" &&
                        file.Visibility == FileVisibility.Private && file.StorageProvider == "LocalPrivate"
                    select new { resource, institution };
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.resource.Title.Contains(term) ||
                x.resource.Topic != null && x.resource.Topic.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.resource.PublishedAtUtc)
            .ThenByDescending(x => x.resource.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new TeacherPublicResourceDto
            {
                Reference = x.resource.PublicId, Institution = x.institution.Name,
                Title = x.resource.Title, KindCode = x.resource.KindCode,
                Topic = x.resource.Topic, LanguageCode = x.resource.LanguageCode,
                SourceUrl = x.resource.SourceUrl!, RightsHolder = x.resource.RightsHolder,
                LicenceCode = x.resource.LicenceCode,
                PublishedAtUtc = x.resource.PublishedAtUtc,
                Subject = db.Subjects.IgnoreQueryFilters().Where(subject =>
                    subject.TenantId == x.resource.TenantId &&
                    subject.Id == x.resource.SubjectId && !subject.IsDeleted)
                    .Select(subject => subject.Name).FirstOrDefault(),
                Level = db.AcademicLevels.IgnoreQueryFilters().Where(level =>
                    level.TenantId == x.resource.TenantId &&
                    level.Id == x.resource.AcademicLevelId && !level.IsDeleted)
                    .Select(level => level.Name).FirstOrDefault()
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<TeacherPublicResourceDto>>.SuccessResponse(
            new PagedResult<TeacherPublicResourceDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<TeacherResourceDto>> UploadAsync(
        TeacherResourceUploadRequestDto request, PrivateFileUploadDto file, CancellationToken ct = default)
    {
        if (!Teacher) return Error<TeacherResourceDto>("Active teacher required.", 403);
        if (!Valid(request) || file == null || file.Length is < 1 or > 10_485_760)
            return Error<TeacherResourceDto>("Valid resource metadata, licence and file required.");
        var authorId = await db.EmployeeCampusAssignments.AsNoTracking().Where(x =>
            x.TenantId == Tenant && x.CampusId == request.CampusId &&
            x.IsCurrent && !x.IsDeleted && x.EffectiveFrom <= DateOnly.FromDateTime(Now) &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo >= DateOnly.FromDateTime(Now)) &&
            MyActiveEmployees().Contains(x.EmployeeId))
            .OrderBy(x => x.EmployeeId).Select(x => x.EmployeeId).FirstOrDefaultAsync(ct);
        if (authorId == 0)
            return Error<TeacherResourceDto>("Active campus assignment required.", 403);
        if (!await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == Tenant &&
            x.Id == request.CampusId && x.IsActive && !x.IsDeleted, ct))
            return Error<TeacherResourceDto>("Campus unavailable.", 404);
        if (request.SubjectId.HasValue && !await db.Subjects.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.Id == request.SubjectId && !x.IsDeleted && x.IsActive, ct))
            return Error<TeacherResourceDto>("Subject unavailable.", 404);
        if (request.AcademicLevelId.HasValue && !await db.AcademicLevels.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.Id == request.AcademicLevelId && !x.IsDeleted && x.IsActive, ct))
            return Error<TeacherResourceDto>("Class/level unavailable.", 404);
        long? planId = null;
        if (request.LessonPlanReference.HasValue)
        {
            planId = await (from plan in db.LessonPlans.AsNoTracking()
                join offering in db.SubjectOfferings.AsNoTracking() on new { plan.TenantId, Id = plan.SubjectOfferingId }
                    equals new { offering.TenantId, offering.Id }
                join batch in db.AcademicBatches.AsNoTracking() on new { offering.TenantId, Id = offering.AcademicBatchId }
                    equals new { batch.TenantId, Id = (long?)batch.Id }
                where plan.TenantId == Tenant && plan.PublicId == request.LessonPlanReference.Value &&
                    plan.EmployeeId == authorId && !plan.IsDeleted && !offering.IsDeleted &&
                    batch.CampusId == request.CampusId && !batch.IsDeleted
                select (long?)plan.Id).FirstOrDefaultAsync(ct);
            if (!planId.HasValue) return Error<TeacherResourceDto>("Own lesson plan in this campus required.", 404);
        }
        var staged = await files.UploadForLmsAsync(request.RequestId, file, ct);
        if (!staged.Success || staged.Data == null)
            return Error<TeacherResourceDto>(staged.Message, staged.StatusCode);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                if (!await MyCampuses().ContainsAsync(request.CampusId, token) ||
                    !await MyActiveEmployees().ContainsAsync(authorId, token))
                    return Error<TeacherResourceDto>("Campus assignment changed.", 409);
                var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == staged.Data.Id && x.PublicId == request.RequestId &&
                    x.UploadedByUserId == user.UserId && !x.IsDeleted &&
                    x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                    x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate" &&
                    x.StorageKey.StartsWith("tenant-" + Tenant + "/lms/"), token);
                if (asset == null) return Error<TeacherResourceDto>("Scanned private file unavailable.", 403);
                var existing = await db.TeacherResources.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.PublicId == request.RequestId && !x.IsDeleted, token);
                if (existing != null)
                {
                    if (existing.FileAssetId != asset.Id || existing.AuthorEmployeeId != authorId ||
                        existing.CampusId != request.CampusId || existing.Title != request.Title.Trim() ||
                        existing.ScopeCode != request.ScopeCode || existing.KindCode != request.KindCode ||
                        existing.LicenceCode != request.LicenceCode ||
                        existing.LicenceEvidence != request.LicenceEvidence.Trim() ||
                        existing.SubjectId != request.SubjectId ||
                        existing.AcademicLevelId != request.AcademicLevelId ||
                        existing.LessonPlanId != planId ||
                        existing.Topic != request.Topic?.Trim() ||
                        existing.SourceUrl != request.SourceUrl?.Trim() ||
                        existing.LanguageCode != request.LanguageCode ||
                        existing.RightsHolder != request.RightsHolder.Trim() ||
                        existing.ExpiresAtUtc != request.ExpiresAtUtc ||
                        existing.SupersedesResourceId.HasValue != request.SupersedesReference.HasValue)
                        return Error<TeacherResourceDto>("Request ID reused with different content.", 409);
                    return ApiResponse<TeacherResourceDto>.SuccessResponse(await MapAsync(existing, token));
                }
                if (await db.TeacherResources.AsNoTracking().AnyAsync(x =>
                    x.TenantId == Tenant && x.FileAssetId == asset.Id && !x.IsDeleted, token))
                    return Error<TeacherResourceDto>("File already registered.", 409);
                TeacherResource? predecessor = null;
                if (request.SupersedesReference.HasValue)
                {
                    predecessor = await db.TeacherResources.FirstOrDefaultAsync(x =>
                        x.TenantId == Tenant && x.PublicId == request.SupersedesReference &&
                        !x.IsDeleted, token);
                    if (predecessor == null || predecessor.AuthorEmployeeId != authorId ||
                        predecessor.CampusId != request.CampusId || predecessor.StateCode != "Approved" ||
                        !Version(predecessor.RowVersion, request.SupersedesRowVersion))
                        return Error<TeacherResourceDto>("Approved own version and current row version required.", 409);
                    if (await db.TeacherResources.AsNoTracking().AnyAsync(x =>
                        x.TenantId == Tenant && x.SupersedesResourceId == predecessor.Id && !x.IsDeleted, token))
                        return Error<TeacherResourceDto>("A revision is already in progress.", 409);
                }
                var row = new TeacherResource
                {
                    TenantId = Tenant, PublicId = request.RequestId,
                    AuthorEmployeeId = authorId, CampusId = request.CampusId,
                    LessonPlanId = planId, AcademicLevelId = request.AcademicLevelId,
                    SubjectId = request.SubjectId, FileAssetId = asset.Id,
                    SupersedesResourceId = predecessor?.Id,
                    VersionNo = predecessor == null ? 1 : predecessor.VersionNo + 1,
                    Title = request.Title.Trim(), KindCode = request.KindCode,
                    ScopeCode = request.ScopeCode, StateCode = "Draft",
                    Topic = request.Topic?.Trim(), LanguageCode = request.LanguageCode,
                    SourceUrl = request.SourceUrl?.Trim(), RightsHolder = request.RightsHolder.Trim(),
                    LicenceCode = request.LicenceCode,
                    LicenceEvidence = request.LicenceEvidence.Trim(),
                    ExpiresAtUtc = request.ExpiresAtUtc,
                    CreatedBy = user.UserId, CreatedAt = Now
                };
                db.TeacherResources.Add(row);
                await db.SaveChangesAsync(token);
                db.AuditLogs.Add(Audit(row, "TeacherResource.Drafted"));
                await db.SaveChangesAsync(token);
                return ApiResponse<TeacherResourceDto>.SuccessResponse(await MapAsync(row, token));
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Teacher resource registration collision for tenant {Tenant}", Tenant);
            return Error<TeacherResourceDto>("Concurrent resource registration; reload and retry.", 409);
        }
    }

    public Task<ApiResponse<TeacherResourceDto>> SubmitAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, CancellationToken ct = default) =>
        TransitionAsync(reference, request, "Submitted", ct);

    public Task<ApiResponse<TeacherResourceDto>> WithdrawAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, CancellationToken ct = default) =>
        TransitionAsync(reference, request, "Withdrawn", ct);

    private async Task<ApiResponse<TeacherResourceDto>> TransitionAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, string target, CancellationToken ct)
    {
        if (!Manager && !Teacher) return Error<TeacherResourceDto>("Teacher access required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.Note?.Length > 500 || (target == "Withdrawn" && string.IsNullOrWhiteSpace(request.Note)))
            return Error<TeacherResourceDto>("Resource, row version and withdrawal reason required.");
        try { return await db.ExecuteInTransactionAsync(async token =>
        {
            var row = await db.TeacherResources.FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted, token);
            if (row == null) return Error<TeacherResourceDto>("Resource not found.", 404);
            if ((target == "Submitted" || !Manager) &&
                !await MyActiveEmployees().ContainsAsync(row.AuthorEmployeeId, token))
                return Error<TeacherResourceDto>("Resource not found.", 404);
            if (row.StateCode == target)
                return ApiResponse<TeacherResourceDto>.SuccessResponse(await MapAsync(row, token));
            if (!Version(row.RowVersion, request.RowVersion))
                return Error<TeacherResourceDto>("Resource changed. Reload and retry.", 409);
            if (target == "Submitted" && row.StateCode is not ("Draft" or "Rejected") ||
                target == "Withdrawn" && row.StateCode is not ("Approved" or "Draft" or "Rejected"))
                return Error<TeacherResourceDto>("Invalid resource transition.", 409);
            row.StateCode = target;
            if (target == "Withdrawn")
            {
                row.WithdrawnAtUtc = Now;
                row.ReviewNote = request.Note?.Trim();
            }
            row.UpdatedAt = Now; row.UpdatedBy = user.UserId;
            db.AuditLogs.Add(Audit(row, target == "Submitted" ? "TeacherResource.Submitted" : "TeacherResource.Withdrawn"));
            await db.SaveChangesAsync(token);
            return ApiResponse<TeacherResourceDto>.SuccessResponse(await MapAsync(row, token));
        }, IsolationLevel.Serializable, ct); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Teacher resource transition collision for tenant {Tenant}", Tenant);
            return Error<TeacherResourceDto>("Resource changed concurrently. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<TeacherResourceDto>> ReviewAsync(Guid reference,
        TeacherResourceTransitionRequestDto request, bool approve, CancellationToken ct = default)
    {
        if (!Manager) return Error<TeacherResourceDto>("Institution administrator required.", 403);
        if (reference == Guid.Empty || request == null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.Note?.Length > 500 || (!approve && string.IsNullOrWhiteSpace(request.Note)))
            return Error<TeacherResourceDto>("Current version and rejection reason required.");
        try { return await db.ExecuteInTransactionAsync(async token =>
        {
            var row = await db.TeacherResources.FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.PublicId == reference && !x.IsDeleted, token);
            if (row == null) return Error<TeacherResourceDto>("Resource not found.", 404);
            if (row.CreatedBy == user.UserId)
                return Error<TeacherResourceDto>("Author cannot approve own resource.", 403);
            if (!Version(row.RowVersion, request.RowVersion))
                return Error<TeacherResourceDto>("Resource changed. Reload and retry.", 409);
            if (row.StateCode != "Submitted")
                return Error<TeacherResourceDto>("Only submitted resources can be reviewed.", 409);
            var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == Tenant && x.Id == row.FileAssetId && !x.IsDeleted &&
                x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
                x.Visibility == FileVisibility.Private, token);
            if (asset == null || row.ExpiresAtUtc.HasValue && row.ExpiresAtUtc <= Now)
                return Error<TeacherResourceDto>("Resource is unavailable for approval.", 409);
            if (approve && !await db.AuditLogs.AsNoTracking().AnyAsync(x =>
                x.TenantId == Tenant && x.UserId == user.UserId &&
                x.EntityName == nameof(TeacherResource) && x.EntityId == row.Id &&
                x.Action == "TeacherResource.ReviewViewed" && x.IsSuccess &&
                x.OccurredAt >= (row.UpdatedAt ?? row.CreatedAt), token))
                return Error<TeacherResourceDto>("Inspect the private resource before approval.", 409);
            if (approve && row.SupersedesResourceId.HasValue)
            {
                var previous = await db.TeacherResources.FirstOrDefaultAsync(x =>
                    x.TenantId == Tenant && x.Id == row.SupersedesResourceId.Value && !x.IsDeleted, token);
                if (previous == null || previous.StateCode != "Approved")
                    return Error<TeacherResourceDto>("Previous published version changed.", 409);
                previous.StateCode = "Withdrawn";
                previous.WithdrawnAtUtc = Now;
                previous.UpdatedAt = Now; previous.UpdatedBy = user.UserId;
                db.AuditLogs.Add(Audit(previous, "TeacherResource.Superseded"));
            }
            row.StateCode = approve ? "Approved" : "Rejected";
            row.ReviewedByUserId = user.UserId; row.ReviewedAtUtc = Now;
            row.ReviewNote = request.Note?.Trim();
            if (approve) row.PublishedAtUtc = Now;
            row.UpdatedAt = Now; row.UpdatedBy = user.UserId;
            db.AuditLogs.Add(Audit(row, approve ? "TeacherResource.Approved" : "TeacherResource.Rejected"));
            await db.SaveChangesAsync(token);
            return ApiResponse<TeacherResourceDto>.SuccessResponse(await MapAsync(row, token));
        }, IsolationLevel.Serializable, ct); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Teacher resource review collision for tenant {Tenant}", Tenant);
            return Error<TeacherResourceDto>("Resource changed concurrently. Reload and retry.", 409);
        }
    }

    public async Task<ApiResponse<DocumentFileDownloadDto>> DownloadAsync(Guid reference, CancellationToken ct = default)
    {
        if (!Manager && !Teacher) return Error<DocumentFileDownloadDto>("Teacher access required.", 403);
        var row = await Visible().FirstOrDefaultAsync(x => x.PublicId == reference, ct);
        if (row == null || row.StateCode == "Withdrawn" ||
            !Manager && row.StateCode != "Approved" &&
            !await MyActiveEmployees().ContainsAsync(row.AuthorEmployeeId, ct))
            return Error<DocumentFileDownloadDto>("Resource unavailable.", 404);
        if (row.ExpiresAtUtc.HasValue && row.ExpiresAtUtc <= Now)
            return Error<DocumentFileDownloadDto>("Resource expired.", 404);
        var asset = await db.FileAssets.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == Tenant && x.Id == row.FileAssetId && !x.IsDeleted &&
            x.IsVerifiedSafe && x.MalwareScanStateCode == "Clean" &&
            x.Visibility == FileVisibility.Private && x.StorageProvider == "LocalPrivate", ct);
        if (asset == null || asset.Sha256 == null ||
            !asset.StorageKey.StartsWith("tenant-" + Tenant + "/lms/", StringComparison.Ordinal))
            return Error<DocumentFileDownloadDto>("Private file unavailable.", 404);
        FileDownloadResult? bytes;
        try { bytes = await storage.GetPrivateFileAsync(asset.StorageKey); }
        catch (IOException ex)
        {
            logger.LogWarning(ex, "Teacher resource IO failure for tenant {Tenant}", Tenant);
            return Error<DocumentFileDownloadDto>("Private file unavailable.", 503);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Teacher resource storage denied for tenant {Tenant}", Tenant);
            return Error<DocumentFileDownloadDto>("Private file unavailable.", 503);
        }
        byte[] expectedHash;
        try { expectedHash = Convert.FromHexString(asset.Sha256); }
        catch (FormatException) { return Error<DocumentFileDownloadDto>("File integrity validation failed.", 409); }
        if (bytes == null || bytes.Content.LongLength != asset.SizeBytes ||
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.Content), expectedHash))
            return Error<DocumentFileDownloadDto>("File integrity validation failed.", 409);
        // Revalidate access after an awaited storage read: withdrawal and campus
        // revocation must take effect on the next private-file request.
        var fresh = await Visible().FirstOrDefaultAsync(x => x.Id == row.Id, ct);
        if (fresh == null || fresh.StateCode == "Withdrawn" ||
            !Manager && fresh.StateCode != "Approved" &&
            !await MyActiveEmployees().ContainsAsync(fresh.AuthorEmployeeId, ct) ||
            fresh.ExpiresAtUtc.HasValue && fresh.ExpiresAtUtc <= Now)
            return Error<DocumentFileDownloadDto>("Resource unavailable.", 404);
        var audit = Audit(fresh, Manager && fresh.StateCode == "Submitted" ?
            "TeacherResource.ReviewViewed" : "TeacherResource.Downloaded");
        db.AuditLogs.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex)
        {
            db.Entry(audit).State = EntityState.Detached;
            logger.LogError(ex, "Resource audit persistence failed for tenant {Tenant}", Tenant);
            return Error<DocumentFileDownloadDto>("Download audit unavailable.", 503);
        }
        return ApiResponse<DocumentFileDownloadDto>.SuccessResponse(new DocumentFileDownloadDto
        {
            Content = bytes.Content, ContentType = asset.ContentType,
            FileName = Path.GetFileName(asset.OriginalFileName.Replace('\\', '/'))
        });
    }
}
