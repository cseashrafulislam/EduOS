using EduOS.Core.Common;
using EduOS.Core.DTOs.HR;
using EduOS.Core.Entities.HR;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.HR;

/// <summary>Internal employment terms. HR approval is not a signed legal-document assertion.</summary>
public sealed partial class EmployeeContractService : IEmployeeContractService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public EmployeeContractService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.UserId > 0 &&
        user.TenantId > 0 && db.CurrentTenantId == user.TenantId;
    private static ApiResponse<T> Error<T>(string msg, int code = 400) =>
        ApiResponse<T>.ErrorResponse(msg, code);
    private static bool Version(byte[] actual, string text)
    {
        try { return actual.AsSpan().SequenceEqual(Convert.FromBase64String(text)); }
        catch (FormatException) { return false; }
    }
    private EmployeeContractDto Map(EmployeeContract x, Employee e) => new()
    {
        Reference = x.PublicId, EmployeeReference = e.PublicId,
        EmployeeCode = e.EmployeeCode, EmployeeName = e.FullName,
        ContractCode = x.ContractCode, EffectiveFrom = x.EffectiveFrom,
        EffectiveTo = x.EffectiveTo, Remarks = x.Remarks, State = x.State,
        CanReview = x.State == EmployeeContractState.Draft && x.CreatedBy != user.UserId,
        ReviewedAt = x.ReviewedAt, ReviewReason = x.ReviewReason,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };

    public async Task<ApiResponse<PagedResult<EmployeeContractDto>>> ListAsync(
        Guid employeeReference, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<EmployeeContractDto>>("Tenant admin required.", 403);
        if (employeeReference == Guid.Empty || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return Error<PagedResult<EmployeeContractDto>>("Invalid employee or pagination.");
        var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == employeeReference && !x.IsDeleted, ct);
        if (e == null) return Error<PagedResult<EmployeeContractDto>>("Employee not found.", 404);
        var query = db.EmployeeContracts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && x.EmployeeId == e.Id && !x.IsDeleted);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.EffectiveFrom).ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        var contractIds = items.Select(x => x.Id).ToArray();
        var documents = await (from d in db.Documents.AsNoTracking()
            join f in db.FileAssets.AsNoTracking() on new { d.TenantId, Id = d.FileAssetId }
                equals new { f.TenantId, f.Id }
            where d.TenantId == user.TenantId && d.EntityType == "EmployeeContract" &&
                d.EntityId.HasValue && contractIds.Contains(d.EntityId.Value) &&
                d.DocumentTypeCode == SignedPdfTypeCode && !d.IsDeleted && !f.IsDeleted
            select new { Document = d, File = f }).ToListAsync(ct);
        var signedByContract = documents.GroupBy(x => x.Document.EntityId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.Document.CreatedAt)
                .ThenByDescending(x => x.Document.Id).First());
        var resultItems = items.Select(x =>
        {
            var mapped = Map(x, e);
            if (signedByContract.TryGetValue(x.Id, out var signed))
                mapped.SignedPdf = MapSignedPdf(signed.Document, signed.File);
            return mapped;
        }).ToList();
        return ApiResponse<PagedResult<EmployeeContractDto>>.SuccessResponse(new PagedResult<EmployeeContractDto>
        { Items = resultItems, TotalCount = total, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<EmployeeContractDto>> CreateAsync(
        CreateEmployeeContractRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<EmployeeContractDto>("Tenant admin required.", 403);
        var code = request?.ContractCode?.Trim() ?? "";
        var remarks = string.IsNullOrWhiteSpace(request?.Remarks) ? null : request.Remarks.Trim();
        if (request == null || request.ClientRequestId == Guid.Empty ||
            request.EmployeeReference == Guid.Empty || code.Length is < 1 or > 50 ||
            request.EffectiveFrom == default || request.EffectiveTo < request.EffectiveFrom ||
            remarks?.Length > 500)
            return Error<EmployeeContractDto>("Invalid contract reference or dates.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var replay = await db.EmployeeContracts.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.ClientRequestId, ct);
            if (replay != null)
            {
                var oldEmployee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == replay.EmployeeId, ct);
                if (oldEmployee == null || oldEmployee.PublicId != request.EmployeeReference ||
                    replay.ContractCode != code || replay.EffectiveFrom != request.EffectiveFrom ||
                    replay.EffectiveTo != request.EffectiveTo || replay.Remarks != remarks)
                    return Error<EmployeeContractDto>("Request key already used for a different contract.", 409);
                return ApiResponse<EmployeeContractDto>.SuccessResponse(Map(replay, oldEmployee));
            }
            var e = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == request.EmployeeReference &&
                x.State == EmployeeState.Active && !x.IsDeleted, ct);
            if (e == null) return Error<EmployeeContractDto>("Active employee not found.", 404);
            if (request.EffectiveFrom < e.JoiningDate || e.LeavingDate.HasValue &&
                request.EffectiveFrom > e.LeavingDate.Value)
                return Error<EmployeeContractDto>("Contract starts outside employment.", 409);
            var contract = new EmployeeContract
            {
                TenantId = user.TenantId, EmployeeId = e.Id, ContractCode = code,
                ClientRequestId = request.ClientRequestId, EffectiveFrom = request.EffectiveFrom,
                EffectiveTo = request.EffectiveTo, Remarks = remarks,
                CreatedAt = clock.GetUtcNow().UtcDateTime, CreatedBy = user.UserId
            };
            db.EmployeeContracts.Add(contract);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<EmployeeContractDto>.SuccessResponse(Map(contract, e));
        }
        catch (DbUpdateException) { return Error<EmployeeContractDto>("Contract duplicate or database conflict.", 409); }
    }

    public async Task<ApiResponse<EmployeeContractDto>> ReviewAsync(
        Guid reference, ReviewEmployeeContractRequestDto request, bool approve,
        CancellationToken ct = default)
    {
        if (!Allowed) return Error<EmployeeContractDto>("Tenant admin required.", 403);
        if (reference == Guid.Empty || request == null ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length > 256 ||
            request.Reason?.Length > 500 || !approve && string.IsNullOrWhiteSpace(request.Reason))
            return Error<EmployeeContractDto>("Version or review reason is invalid.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var row = await db.EmployeeContracts.SingleOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.PublicId == reference && !x.IsDeleted, ct);
            if (row == null) return Error<EmployeeContractDto>("Contract not found.", 404);
            if (row.State != EmployeeContractState.Draft || !Version(row.RowVersion, request.RowVersion))
                return Error<EmployeeContractDto>("Contract was already reviewed or changed.", 409);
            if (row.CreatedBy == user.UserId)
                return Error<EmployeeContractDto>("Creator cannot approve or reject own contract.", 403);
            var employee = await db.Employees.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.Id == row.EmployeeId && !x.IsDeleted, ct);
            if (employee == null) return Error<EmployeeContractDto>("Employee not found.", 404);
            if (approve && await db.EmployeeContracts.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.EmployeeId == row.EmployeeId &&
                x.Id != row.Id && x.State == EmployeeContractState.Approved && !x.IsDeleted &&
                (!x.EffectiveTo.HasValue || x.EffectiveTo >= row.EffectiveFrom) &&
                (!row.EffectiveTo.HasValue || x.EffectiveFrom <= row.EffectiveTo.Value), ct))
                return Error<EmployeeContractDto>("Approved contract dates overlap existing terms.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            row.State = approve ? EmployeeContractState.Approved : EmployeeContractState.Rejected;
            row.ReviewedByUserId = user.UserId; row.ReviewedAt = now;
            row.ReviewReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();
            row.UpdatedAt = now; row.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<EmployeeContractDto>.SuccessResponse(Map(row, employee));
        }
        catch (DbUpdateConcurrencyException) { return Error<EmployeeContractDto>("Contract changed concurrently.", 409); }
        catch (DbUpdateException) { return Error<EmployeeContractDto>("Contract conflicts with saved history.", 409); }
    }
}
