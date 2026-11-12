using System.Data;
using System.Text.Json;
using EduOS.Core.Common;
using EduOS.Core.DTOs.SaaS;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace EduOS.Service.Services.Tenants;

/// <summary>
/// One tenant, one reviewed preset preview and one explicitly confirmed additive transaction.
/// Existing domain tables remain authoritative; no shadow master records or financial data.
/// </summary>
public sealed class SmartSetupService(
    EduOSDbContext db, ITenantModuleService modules, IUnitOfWork unitOfWork,
    ICurrentUserService user, ILogger<SmartSetupService> logger,
    IConfiguration? configuration = null)
{
    private sealed record Existing(string Kind, string Code, string Name, bool Active);
    private sealed record ApplyReceipt(string Fingerprint, SmartSetupApplyResultDto Result);
    private static readonly HashSet<string> PilotExcluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "LMS", "LIBRARY", "HOSTEL", "TRANSPORT", "INVENTORY",
        "FACILITIES", "ALUMNI", "RESEARCH", "ACCREDITATION"
    };
    private sealed class SetupConflict(string message, int status = 409) : Exception(message)
    {
        public int Status { get; } = status;
    }

    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin &&
        user.TenantId > 0 && db.CurrentTenantId == user.TenantId;

    public async Task<ApiResponse<SmartSetupPreviewDto>> PreviewAsync(CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<SmartSetupPreviewDto>.ErrorResponse("Tenant administrator permission required.", 403);
        try
        {
            return ApiResponse<SmartSetupPreviewDto>.SuccessResponse(await PreviewCoreAsync(ct));
        }
        catch (SetupConflict ex)
        {
            return ApiResponse<SmartSetupPreviewDto>.ErrorResponse(ex.Message, ex.Status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Smart setup preview failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<SmartSetupPreviewDto>.ErrorResponse("Setup preview could not be loaded.", 500);
        }
    }

    public async Task<ApiResponse<SmartSetupApplyResultDto>> ApplyAsync(
        ApplySmartSetupRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Tenant administrator permission required.", 403);
        if (request == null || !request.Confirmed || request.ClientRequestId == Guid.Empty ||
            request.InstitutionTypeId <= 0 || request.TemplateVersion != SmartSetupCatalog.Version ||
            request.Modules == null || request.Modules.Count is < 1 or > 7 ||
            request.Modules.Any(x => string.IsNullOrWhiteSpace(x)))
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Review the current template and explicitly confirm the selected modules.");

        var selection = request.Modules.Select(x => x.Trim().ToUpperInvariant()).ToArray();
        if (selection.Distinct(StringComparer.Ordinal).Count() != selection.Length ||
            selection.Any(x => !SmartSetupCatalog.Sections.Any(s => s.Apply && s.Code == x)))
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Unknown or duplicate setup module.");

        // Legacy callers still select whole modules. A new UI submits exact
        // template keys; empty/duplicate/mismatched selections fail closed.
        var requestedRows = request.Entries;
        if (requestedRows != null && (requestedRows.Count is < 1 or > 50 ||
            requestedRows.Any(x => x == null || string.IsNullOrWhiteSpace(x.Module) ||
                string.IsNullOrWhiteSpace(x.Kind) || string.IsNullOrWhiteSpace(x.Code) ||
                x.Module.Length > 32 || x.Kind.Length > 64 || x.Code.Length > 64)))
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Select valid reviewed setup rows.");
        var rows = requestedRows?.Select(x => new SmartSetupSelectedEntryDto
        {
            Module = x.Module.Trim().ToUpperInvariant(),
            Kind = x.Kind.Trim(), Code = x.Code.Trim().ToUpperInvariant()
        }).ToArray();
        if (rows != null && (rows.Select(x => x.Module + "|" + x.Kind + "|" + x.Code)
                .Distinct(StringComparer.Ordinal).Count() != rows.Length ||
            rows.Any(x => !selection.Contains(x.Module, StringComparer.Ordinal)) ||
            selection.Any(x => !rows.Any(r => r.Module == x))))
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Duplicate or mismatched setup rows.");

        try
        {
            // Revalidate every entitlement inside the transaction, never trust preview or UI.
            return await unitOfWork.ExecuteInTransactionAsync(async token =>
            {
                await LockTenantAsync(token);
                var fingerprint = request.InstitutionTypeId.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "|" + request.TemplateVersion + "|" +
                    string.Join(",", selection.OrderBy(x => x, StringComparer.Ordinal)) +
                    (rows == null ? "" : "|rows:" + string.Join(";", rows
                        .Select(x => x.Module + "|" + x.Kind + "|" + x.Code)
                        .OrderBy(x => x, StringComparer.Ordinal)));
                var receiptText = await db.AuditLogs.AsNoTracking()
                    .Where(x => x.TenantId == user.TenantId && x.Action == "SmartSetup.Apply" &&
                        x.RequestId == request.ClientRequestId.ToString("D"))
                    .Select(x => x.NewValue).FirstOrDefaultAsync(token);
                if (receiptText != null)
                {
                    ApplyReceipt? receipt;
                    try { receipt = JsonSerializer.Deserialize<ApplyReceipt>(receiptText); }
                    catch (JsonException) { throw new SetupConflict("Request reference was already used. Start a new review."); }
                    if (receipt == null || receipt.Fingerprint != fingerprint || receipt.Result == null)
                        throw new SetupConflict("Request reference was used for different setup data.");
                    return ApiResponse<SmartSetupApplyResultDto>.SuccessResponse(receipt.Result, "Setup request already applied.");
                }
                var preview = await PreviewCoreAsync(token);
                if (preview.InstitutionTypeId == 0 || preview.InstitutionTypeId != request.InstitutionTypeId)
                    throw new SetupConflict("Institution type changed. Reload the setup preview.");
                foreach (var name in selection)
                {
                    var section = preview.Modules.Single(x => x.Code == name);
                    if (!section.IsAvailable)
                        throw new SetupConflict("Module is disabled or not included in the active subscription.", 403);
                    if (rows == null && section.Entries.Any(x => x.Status == "Conflict"))
                        throw new SetupConflict("Existing master data conflicts with this template. Review it before applying.");
                    if (rows != null)
                    {
                        foreach (var key in rows.Where(x => x.Module == name))
                        {
                            var match = section.Entries.SingleOrDefault(x =>
                                x.Kind == key.Kind && x.Code == key.Code);
                            if (match == null)
                                throw new SetupConflict("Selected setup row is not in this institution template.");
                            if (match.Status == "Conflict")
                                throw new SetupConflict("Selected row conflicts with existing master data.");
                            if (match.Status != "Missing" && match.Status != "Existing")
                                throw new SetupConflict("Selected setup row is no longer available.", 403);
                        }
                    }
                }
                var created = new List<SmartSetupEntryDto>();
                var existing = 0;
                var now = DateTime.UtcNow;
                foreach (var name in selection)
                {
                    foreach (var candidate in preview.Modules.Single(x => x.Code == name).Entries
                        .Where(x => rows == null || rows.Any(r => r.Module == name &&
                            r.Kind == x.Kind && r.Code == x.Code)))
                    {
                        if (candidate.Status == "Existing") { existing++; continue; }
                        if (candidate.Status != "Missing") throw new SetupConflict("Setup changed. Reload the preview.");
                        AddCanonicalMaster(candidate, now);
                        created.Add(new SmartSetupEntryDto
                        {
                            Kind = candidate.Kind, Code = candidate.Code, Name = candidate.Name, Status = "Created"
                        });
                    }
                }
                var outcome = new SmartSetupApplyResultDto
                {
                    CreatedCount = created.Count, ExistingCount = existing,
                    TemplateVersion = SmartSetupCatalog.Version, Created = created
                };
                // Persist a write-once result even for a no-op apply. The per-tenant
                // transaction lock serializes replays; changed payloads are rejected.
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = user.TenantId, UserId = user.UserId,
                    Action = "SmartSetup.Apply", EntityName = "InstitutionSetup",
                    IsSuccess = true, OccurredAt = now,
                    RequestId = request.ClientRequestId.ToString("D"),
                    NewValue = JsonSerializer.Serialize(new ApplyReceipt(fingerprint, outcome))
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<SmartSetupApplyResultDto>.SuccessResponse(outcome,
                    created.Count == 0 ? "Already configured. No changes made." : "Selected safe defaults saved.");
            }, IsolationLevel.Serializable, ct);
        }
        catch (SetupConflict ex)
        {
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse(ex.Message, ex.Status);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Setup changed. Reload the preview.", 409);
        }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Smart setup database conflict for tenant {TenantId}", user.TenantId);
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Setup data conflicts with existing records. Reload the preview.", 409);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Smart setup apply failed for tenant {TenantId}", user.TenantId);
            return ApiResponse<SmartSetupApplyResultDto>.ErrorResponse("Setup was not applied. No partial changes were committed.", 500);
        }
    }

    private async Task<SmartSetupPreviewDto> PreviewCoreAsync(CancellationToken ct)
    {
        var tenant = await db.Tenants.AsNoTracking()
            .Where(x => x.Id == user.TenantId && !x.IsDeleted)
            .Select(x => new { x.InstitutionTypeDefinitionId, x.CountryCode }).SingleOrDefaultAsync(ct);
        if (tenant == null) throw new SetupConflict("Institution not found.", 404);
        var institutionType = tenant.InstitutionTypeDefinitionId.HasValue
            ? await db.InstitutionTypeDefinitions.AsNoTracking()
                .Where(x => x.Id == tenant.InstitutionTypeDefinitionId && !x.IsDeleted && x.IsActive)
                .Select(x => new { x.Id, x.Code, x.Name }).SingleOrDefaultAsync(ct)
            : null;
        var result = new SmartSetupPreviewDto
        {
            InstitutionTypeId = institutionType?.Id ?? 0,
            InstitutionTypeCode = institutionType?.Code ?? "UNSPECIFIED",
            InstitutionTypeName = institutionType?.Name ?? "Institution type not selected",
            CountryCode = tenant.CountryCode,
            TemplateVersion = SmartSetupCatalog.Version
        };
        var templates = institutionType == null
            ? new List<SmartSetupCatalog.Seed>()
            : SmartSetupCatalog.Build(institutionType.Code, tenant.CountryCode).ToList();
        var matches = await FindExistingAsync(user.TenantId, templates, ct);
        foreach (var section in SmartSetupCatalog.Sections)
        {
            // Manual-only modules are listed for guided setup; never claim their
            // operational readiness without checking their owning workflows.
            var accessCode = SmartSetupCatalog.AccessModule(section.Code);
            var pilotExcluded = configuration?.GetValue<bool>("PilotRelease:Enabled") == true &&
                PilotExcluded.Contains(accessCode);
            var available = institutionType != null && !pilotExcluded &&
                await modules.IsCurrentTenantModuleAvailableAsync(accessCode);
            var module = new SmartSetupModuleDto
            {
                Code = section.Code, Name = section.Name, NameBangla = section.Bangla,
                NextStepUrl = section.Url, SupportsSafeApply = section.Apply,
                IsAvailable = available, Guidance = section.Guidance
            };
            foreach (var seed in templates.Where(x => x.Module == section.Code))
            {
                var competing = matches.Where(x => x.Kind == seed.Kind &&
                    (string.Equals(x.Code, seed.Code, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(x.Name, seed.Name, StringComparison.OrdinalIgnoreCase))).ToList();
                var same = competing.Count == 1 && competing[0].Active &&
                    string.Equals(competing[0].Code, seed.Code, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(competing[0].Name, seed.Name, StringComparison.OrdinalIgnoreCase);
                module.Entries.Add(new SmartSetupEntryDto
                {
                    Kind = seed.Kind, Code = seed.Code, Name = seed.Name,
                    Detail = seed.Frequency?.ToString(),
                    Status = !available ? "Disabled" : same ? "Existing" :
                        competing.Count == 0 ? "Missing" : "Conflict"
                });
            }
            result.Modules.Add(module);
        }
        return result;
    }

    private async Task<List<Existing>> FindExistingAsync(long tenant, List<SmartSetupCatalog.Seed> templates,
        CancellationToken ct)
    {
        var all = new List<Existing>();
        foreach (var kind in templates.Select(x => x.Kind).Distinct())
        {
            var entries = templates.Where(x => x.Kind == kind).ToArray();
            var codes = entries.Select(x => x.Code).ToArray();
            var names = entries.Select(x => x.Name).ToArray();
            switch (kind)
            {
                case "Medium":
                    all.AddRange(await db.Mediums.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("Medium", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "OrganizationUnit":
                    all.AddRange(await db.OrganizationUnits.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("OrganizationUnit", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "Designation":
                    all.AddRange(await db.Designations.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("Designation", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "FeeHead":
                    all.AddRange(await db.FeeHeads.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("FeeHead", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "AssetCategory":
                    all.AddRange(await db.AssetCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("AssetCategory", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "BookCategory":
                    all.AddRange(await db.BookCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("BookCategory", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "NoticeCategory":
                    all.AddRange(await db.NoticeCategories.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("NoticeCategory", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                case "DocumentType":
                    all.AddRange(await db.DocumentTypeDefinitions.IgnoreQueryFilters().AsNoTracking().Where(x => x.TenantId == tenant &&
                        (codes.Contains(x.Code) || names.Contains(x.Name)))
                        .Select(x => new Existing("DocumentType", x.Code, x.Name, x.IsActive && !x.IsDeleted)).ToListAsync(ct));
                    break;
                default:
                    throw new InvalidOperationException("Unrecognized reviewed preset kind.");
            }
        }
        return all;
    }

    private void AddCanonicalMaster(SmartSetupEntryDto row, DateTime now)
    {
        var tenant = user.TenantId;
        switch (row.Kind)
        {
            case "Medium":
                db.Mediums.Add(new Medium
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsDefault = false, IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "OrganizationUnit":
                db.OrganizationUnits.Add(new OrganizationUnit
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "Designation":
                db.Designations.Add(new Designation
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "FeeHead":
                // Frequency is fixed by the reviewed catalog, not supplied by the browser.
                // The authoritative frequency mapping is stable for v1 even when tenant
                // lookup has not been tracked; no financial amount/ledger is created.
                db.FeeHeads.Add(new FeeHead
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    // The server-reviewed catalog is the sole source of the
                    // recommended frequency. The browser submits keys only.
                    DefaultFrequency = Enum.Parse<EduOS.Core.Enums.Domain.FeeFrequencyType>(
                        row.Detail ?? throw new InvalidOperationException(
                            "Reviewed fee frequency is missing.")),
                    IsRefundable = false, IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "AssetCategory":
                db.AssetCategories.Add(new AssetCategory
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "BookCategory":
                db.BookCategories.Add(new BookCategory
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "NoticeCategory":
                db.NoticeCategories.Add(new NoticeCategory
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            case "DocumentType":
                db.DocumentTypeDefinitions.Add(new DocumentTypeDefinition
                {
                    TenantId = tenant, Code = row.Code, Name = row.Name,
                    RequiresVerification = false, IsSensitive = false,
                    IsActive = true, CreatedAt = now, CreatedBy = user.UserId
                });
                break;
            default: throw new InvalidOperationException("Unrecognized reviewed preset kind.");
        }
    }

    private async Task LockTenantAsync(CancellationToken ct)
    {
        if (!db.Database.IsRelational()) return; // Deterministic in-memory service tests.
        if (!db.Database.IsSqlServer())
            throw new InvalidOperationException("Safe concurrent setup requires SQL Server.");
        var transaction = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("Smart setup must execute inside a transaction.");
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "DECLARE @result int; EXEC @result = sys.sp_getapplock " +
            "@Resource = @resource, @LockMode = 'Exclusive', @LockOwner = 'Transaction', " +
            "@LockTimeout = 10000; SELECT @result;";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@resource";
        parameter.Value = "EduOS.SmartSetup.Tenant." + user.TenantId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        command.Parameters.Add(parameter);
        if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) < 0)
            throw new SetupConflict("Another setup operation is running. Retry after it completes.");
    }
}
