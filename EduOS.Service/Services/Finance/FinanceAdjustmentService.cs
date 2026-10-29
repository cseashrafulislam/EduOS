using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

/// <summary>Institutional fee adjustments, including controlled cash refunds; never Platform SaaS billing.</summary>
public sealed partial class FinanceAdjustmentService : IFinanceAdjustmentService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public FinanceAdjustmentService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool Authorized => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin &&
        db.CurrentTenantId == user.TenantId;
    private static ApiResponse<T> Error<T>(string message, int status = 400) => ApiResponse<T>.ErrorResponse(message, status);
    private static string Version(byte[] value) => Convert.ToBase64String(value);
    private static bool Matches(byte[] actual, string? version)
    { try { return !string.IsNullOrWhiteSpace(version) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(version)); } catch (FormatException) { return false; } }
    private static bool Scale(decimal value, int digits) => decimal.Round(value, digits) == value;
    private IQueryable<DiscountRule> Discounts => db.DiscountRules.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<StudentDiscount> Assignments => db.StudentDiscounts.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<FineRule> Fines => db.FineRules.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<StudentFine> StudentFines => db.StudentFines.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<StudentEnrollment> Enrollments => db.StudentEnrollments.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<FeeHead> Heads => db.FeeHeads.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private IQueryable<Refund> Refunds => db.Refunds.Where(x => x.TenantId == user.TenantId && !x.IsDeleted);
    private static DiscountRuleDto Map(DiscountRule x) => new() { Id=x.Id, Name=x.Name, FeeHeadId=x.FeeHeadId, IsPercentage=x.IsPercentage, Value=x.Value, EffectiveFrom=x.EffectiveFrom, EffectiveTo=x.EffectiveTo, IsActive=x.IsActive, RowVersion=Version(x.RowVersion) };
    private static FineRuleDto Map(FineRule x) => new() { Id=x.Id, Name=x.Name, FeeHeadId=x.FeeHeadId, Amount=x.Amount, IsDaily=x.IsDaily, GraceDays=x.GraceDays, IsActive=x.IsActive, RowVersion=Version(x.RowVersion) };
    private static StudentDiscountDto Map(StudentDiscount x, Guid reference, string name) => new() { Id=x.Id, StudentEnrollmentReference=reference, DiscountRuleId=x.DiscountRuleId, DiscountRuleName=name, EffectiveFrom=x.EffectiveFrom, EffectiveTo=x.EffectiveTo, ApprovedByUserId=x.ApprovedByUserId, Reason=x.Reason, IsActive=x.IsActive, RowVersion=Version(x.RowVersion) };
    private static StudentFineDto Map(StudentFine x, Guid reference, string name, Guid? invoiceReference=null) => new() { Id=x.Id, StudentEnrollmentReference=reference, FineRuleId=x.FineRuleId, FineRuleName=name, FineDate=x.FineDate, Amount=x.Amount, Reason=x.Reason, IsWaived=x.IsWaived, WaivedByUserId=x.WaivedByUserId, BilledInvoiceReference=invoiceReference, RowVersion=Version(x.RowVersion) };
    private static RefundDto Map(Refund x, Guid paymentReference) => new() { Id=x.Id, StudentPaymentReference=paymentReference, Amount=x.Amount, State=x.State, Reason=x.Reason, RequestedByUserId=x.RequestedByUserId, ApprovedByUserId=x.ApprovedByUserId, ApprovedAt=x.ApprovedAt, PaidAt=x.PaidAt, ReversalJournalId=x.ReversalJournalId, RowVersion=Version(x.RowVersion) };
    private static bool Dates(DateOnly? from, DateOnly? to) => !from.HasValue || !to.HasValue || from <= to;
    private static bool Overlap(DateOnly a, DateOnly? b, DateOnly c, DateOnly? d) =>
        (!b.HasValue || c <= b.Value) && (!d.HasValue || a <= d.Value);
    private async Task<bool> ValidHead(long? id, CancellationToken ct) => !id.HasValue ||
        await Heads.AnyAsync(x => x.Id == id && x.IsActive, ct);
    private Task<StudentEnrollment?> FindEnrollment(Guid reference, CancellationToken ct) =>
        Enrollments.FirstOrDefaultAsync(x => x.PublicId == reference, ct);
    // Posted/paid invoices remain financial history even when a legacy import
    // subsequently soft-deleted or cancelled the visible invoice record.
    private async Task<bool> HasIssuedInvoice(long enrollmentId, DateOnly from, CancellationToken ct) =>
        await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == user.TenantId && x.StudentEnrollmentId == enrollmentId &&
            x.InvoiceDate >= from &&
            ((!x.IsDeleted && x.State != InvoiceState.Cancelled) ||
             x.JournalId.HasValue || x.PaidAmount > 0m ||
             x.State == InvoiceState.PartiallyPaid || x.State == InvoiceState.Paid ||
             x.State == InvoiceState.Refunded), ct);

    public async Task<ApiResponse<IReadOnlyList<DiscountRuleDto>>> GetDiscountRulesAsync(CancellationToken ct = default)
    {
        if (!Authorized) return Error<IReadOnlyList<DiscountRuleDto>>("Institution administrator permission required.",403);
        var rows=await Discounts.AsNoTracking().OrderBy(x=>x.Name).Take(501).ToListAsync(ct);
        if(rows.Count>500) return Error<IReadOnlyList<DiscountRuleDto>>("Rule list limit exceeded.",409);
        return ApiResponse<IReadOnlyList<DiscountRuleDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }
    public async Task<ApiResponse<DiscountRuleDto>> SaveDiscountRuleAsync(long? ruleId, SaveDiscountRuleRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<DiscountRuleDto>("Institution administrator permission required.",403);
        if(request==null || ruleId is <=0 || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length>150 || !Dates(request.EffectiveFrom,request.EffectiveTo) ||
            request.Value<0 || request.Value>(request.IsPercentage?100m:999999999999m) ||
            !Scale(request.Value,4) || (!request.IsPercentage && !Scale(request.Value,2)))
            return Error<DiscountRuleDto>("Invalid discount rule or decimal precision.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            if(!await ValidHead(request.FeeHeadId,ct)) return Error<DiscountRuleDto>("Fee head not found.",404);
            var row=ruleId.HasValue?await Discounts.FirstOrDefaultAsync(x=>x.Id==ruleId.Value,ct):null;
            if(ruleId.HasValue && row==null) return Error<DiscountRuleDto>("Discount rule not found.",404);
            if(row!=null && !Matches(row.RowVersion,request.RowVersion)) return Error<DiscountRuleDto>("Rule changed; reload.",409);
            var name=request.Name.Trim();
            if(await Discounts.AnyAsync(x=>x.Name==name && (!ruleId.HasValue || x.Id!=ruleId.Value),ct))
                return Error<DiscountRuleDto>("Rule name already exists.",409);
            if(row!=null && await Assignments.AnyAsync(x=>x.DiscountRuleId==row.Id && x.IsActive,ct) &&
                (row.IsPercentage!=request.IsPercentage || row.Value!=request.Value || row.FeeHeadId!=request.FeeHeadId ||
                 row.IsActive!=request.IsActive || row.EffectiveFrom!=request.EffectiveFrom || row.EffectiveTo!=request.EffectiveTo))
                return Error<DiscountRuleDto>("Rule has active assignments; create a new rule instead of changing financial terms.",409);
            var now=clock.GetUtcNow().UtcDateTime;
            if(row==null){row=new DiscountRule{TenantId=user.TenantId,CreatedAt=now,CreatedBy=user.UserId};db.DiscountRules.Add(row);}
            else{row.UpdatedAt=now;row.UpdatedBy=user.UserId;}
            row.Name=name;row.FeeHeadId=request.FeeHeadId;row.IsPercentage=request.IsPercentage;row.Value=request.Value;
            row.EffectiveFrom=request.EffectiveFrom;row.EffectiveTo=request.EffectiveTo;row.IsActive=request.IsActive;
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<DiscountRuleDto>.SuccessResponse(Map(row),"Discount rule saved.");
        }
        catch(DbUpdateConcurrencyException){return Error<DiscountRuleDto>("Rule changed; reload.",409);}
        catch(DbUpdateException){return Error<DiscountRuleDto>("Conflicting discount rule.",409);}
    }
    public async Task<ApiResponse<StudentDiscountDto>> AssignDiscountAsync(AssignStudentDiscountRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<StudentDiscountDto>("Institution administrator permission required.",403);
        if(request==null || request.ClientRequestId==Guid.Empty || request.StudentEnrollmentReference==Guid.Empty ||
            request.DiscountRuleId<=0 || request.EffectiveFrom==default || !Dates(request.EffectiveFrom,request.EffectiveTo) ||
            request.Reason?.Length>1000) return Error<StudentDiscountDto>("Invalid assignment.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var enrollment=await FindEnrollment(request.StudentEnrollmentReference,ct);
            if(enrollment==null) return Error<StudentDiscountDto>("Enrollment not found.",404);
            var replay=await Assignments.FirstOrDefaultAsync(x=>x.ClientRequestId==request.ClientRequestId,ct);
            if(replay!=null)
            {
                if(replay.StudentEnrollmentId!=enrollment.Id || replay.DiscountRuleId!=request.DiscountRuleId ||
                    replay.EffectiveFrom!=request.EffectiveFrom || replay.EffectiveTo!=request.EffectiveTo ||
                    replay.Reason!=request.Reason?.Trim())
                    return Error<StudentDiscountDto>("Idempotency key was reused with different assignment details.",409);
                var historicalRule=await Discounts.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==replay.DiscountRuleId,ct);
                if(historicalRule==null) return Error<StudentDiscountDto>("Original discount rule needs reconciliation.",409);
                return ApiResponse<StudentDiscountDto>.SuccessResponse(Map(replay,enrollment.PublicId,historicalRule.Name),
                    "Existing assignment returned.");
            }
            var rule=await Discounts.FirstOrDefaultAsync(x=>x.Id==request.DiscountRuleId && x.IsActive,ct);
            if(rule==null) return Error<StudentDiscountDto>("Active discount rule not found.",404);
            if((rule.EffectiveFrom.HasValue && request.EffectiveFrom<rule.EffectiveFrom) ||
               (rule.EffectiveTo.HasValue && (!request.EffectiveTo.HasValue || request.EffectiveTo>rule.EffectiveTo)))
                return Error<StudentDiscountDto>("Assignment must stay within the rule's effective dates.");
            if(await HasIssuedInvoice(enrollment.Id,request.EffectiveFrom,ct))
                return Error<StudentDiscountDto>("Invoices already exist from this date; retroactive discounts require an audited billing adjustment.",409);
            var existing=await Assignments.Where(x=>x.StudentEnrollmentId==enrollment.Id && x.DiscountRuleId==rule.Id && x.IsActive).ToListAsync(ct);
            var same=existing.FirstOrDefault(x=>x.EffectiveFrom==request.EffectiveFrom && x.EffectiveTo==request.EffectiveTo && x.Reason==(request.Reason?.Trim()));
            if(same!=null)
            {
                if(same.ClientRequestId.HasValue)
                    return Error<StudentDiscountDto>("An identical assignment already exists with a different request key.",409);
                return ApiResponse<StudentDiscountDto>.SuccessResponse(Map(same,enrollment.PublicId,rule.Name),"Existing legacy assignment returned.");
            }
            if(existing.Any(x=>Overlap(x.EffectiveFrom,x.EffectiveTo,request.EffectiveFrom,request.EffectiveTo)))
                return Error<StudentDiscountDto>("An overlapping assignment already exists.",409);
            var now=clock.GetUtcNow().UtcDateTime;
            var row=new StudentDiscount{TenantId=user.TenantId,ClientRequestId=request.ClientRequestId,
                StudentEnrollmentId=enrollment.Id,DiscountRuleId=rule.Id,
                EffectiveFrom=request.EffectiveFrom,EffectiveTo=request.EffectiveTo,Reason=request.Reason?.Trim(),
                ApprovedByUserId=user.UserId,IsActive=true,CreatedAt=now,CreatedBy=user.UserId};
            db.StudentDiscounts.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<StudentDiscountDto>.SuccessResponse(Map(row,enrollment.PublicId,rule.Name),"Discount assigned for future invoice generation.");
        }
        catch(DbUpdateConcurrencyException){return Error<StudentDiscountDto>("Assignment changed; reload.",409);}
        catch(DbUpdateException){return Error<StudentDiscountDto>("Conflicting assignment.",409);}
    }
    public async Task<ApiResponse<IReadOnlyList<FineRuleDto>>> GetFineRulesAsync(CancellationToken ct = default)
    {
        if(!Authorized) return Error<IReadOnlyList<FineRuleDto>>("Institution administrator permission required.",403);
        var rows=await Fines.AsNoTracking().OrderBy(x=>x.Name).Take(501).ToListAsync(ct);
        if(rows.Count>500) return Error<IReadOnlyList<FineRuleDto>>("Rule list limit exceeded.",409);
        return ApiResponse<IReadOnlyList<FineRuleDto>>.SuccessResponse(rows.Select(Map).ToArray());
    }
    public async Task<ApiResponse<FineRuleDto>> SaveFineRuleAsync(long? ruleId, SaveFineRuleRequestDto request, CancellationToken ct = default)
    {
        if(!Authorized) return Error<FineRuleDto>("Institution administrator permission required.",403);
        if(request==null || ruleId is <=0 || string.IsNullOrWhiteSpace(request.Name) ||
            request.Name.Trim().Length>150 || request.Amount<=0 || request.Amount>999999999999m ||
            !Scale(request.Amount,2) || request.GraceDays is <0 or >3650 || request.IsDaily)
            return Error<FineRuleDto>("Only positive fixed fines are supported; daily fine billing is not integrated.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            if(!await ValidHead(request.FeeHeadId,ct)) return Error<FineRuleDto>("Fee head not found.",404);
            var row=ruleId.HasValue?await Fines.FirstOrDefaultAsync(x=>x.Id==ruleId.Value,ct):null;
            if(ruleId.HasValue && row==null) return Error<FineRuleDto>("Fine rule not found.",404);
            if(row!=null && !Matches(row.RowVersion,request.RowVersion)) return Error<FineRuleDto>("Rule changed; reload.",409);
            var name=request.Name.Trim();
            if(await Fines.AnyAsync(x=>x.Name==name && (!ruleId.HasValue || x.Id!=ruleId.Value),ct))
                return Error<FineRuleDto>("Fine rule name already exists.",409);
            if(row!=null && await StudentFines.AnyAsync(x=>x.FineRuleId==row.Id,ct) &&
                (row.Amount!=request.Amount || row.FeeHeadId!=request.FeeHeadId || row.IsDaily!=request.IsDaily))
                return Error<FineRuleDto>("Applied fine rule terms are immutable; create a new rule.",409);
            var now=clock.GetUtcNow().UtcDateTime;
            if(row==null){row=new FineRule{TenantId=user.TenantId,CreatedAt=now,CreatedBy=user.UserId};db.FineRules.Add(row);}
            else{row.UpdatedAt=now;row.UpdatedBy=user.UserId;}
            row.Name=name;row.FeeHeadId=request.FeeHeadId;row.Amount=request.Amount;row.IsDaily=false;
            row.GraceDays=request.GraceDays;row.IsActive=request.IsActive;
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<FineRuleDto>.SuccessResponse(Map(row),"Fine rule saved.");
        }
        catch(DbUpdateConcurrencyException){return Error<FineRuleDto>("Rule changed; reload.",409);}
        catch(DbUpdateException){return Error<FineRuleDto>("Conflicting fine rule.",409);}
    }
    public async Task<ApiResponse<StudentFineDto>> ApplyFineAsync(ApplyStudentFineRequestDto request, CancellationToken ct = default)
    {
        if(!Authorized) return Error<StudentFineDto>("Institution administrator permission required.",403);
        if(request==null || request.ClientRequestId==Guid.Empty || request.StudentEnrollmentReference==Guid.Empty ||
            request.FineRuleId<=0 || request.FineDate==default || request.Reason?.Length>500)
            return Error<StudentFineDto>("Invalid fine application.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var enrollment=await FindEnrollment(request.StudentEnrollmentReference,ct);
            if(enrollment==null) return Error<StudentFineDto>("Enrollment not found.",404);
            var replay=await StudentFines.FirstOrDefaultAsync(x=>x.ClientRequestId==request.ClientRequestId,ct);
            if(replay!=null)
            {
                if(replay.StudentEnrollmentId!=enrollment.Id || replay.FineRuleId!=request.FineRuleId ||
                    replay.FineDate!=request.FineDate || replay.Reason!=request.Reason?.Trim())
                    return Error<StudentFineDto>("Idempotency key was reused with different fine details.",409);
                var historicalRule=await Fines.AsNoTracking().FirstOrDefaultAsync(x=>x.Id==replay.FineRuleId,ct);
                if(historicalRule==null) return Error<StudentFineDto>("Original fine rule needs reconciliation.",409);
                return ApiResponse<StudentFineDto>.SuccessResponse(Map(replay,enrollment.PublicId,historicalRule.Name),
                    "Existing fine returned.");
            }
            var rule=await Fines.FirstOrDefaultAsync(x=>x.Id==request.FineRuleId && x.IsActive && !x.IsDaily,ct);
            if(rule==null) return Error<StudentFineDto>("Active fixed fine rule not found.",404);
            // A fine is assessed separately and can only affect a new dedicated invoice.
            // Prior issued/paid tuition invoices must remain untouched.
            var reason=request.Reason?.Trim();
            var old=await StudentFines.FirstOrDefaultAsync(x=>x.StudentEnrollmentId==enrollment.Id &&
                x.FineRuleId==rule.Id && x.FineDate==request.FineDate && x.Reason==reason,ct);
            if(old!=null)
            {
                if(old.ClientRequestId.HasValue)
                    return Error<StudentFineDto>("An identical fine already exists with a different request key.",409);
                return ApiResponse<StudentFineDto>.SuccessResponse(Map(old,enrollment.PublicId,rule.Name),"Existing legacy fine returned.");
            }
            var now=clock.GetUtcNow().UtcDateTime;
            var row=new StudentFine{TenantId=user.TenantId,ClientRequestId=request.ClientRequestId,
                StudentEnrollmentId=enrollment.Id,FineRuleId=rule.Id,
                FineDate=request.FineDate,Amount=rule.Amount,Reason=reason,CreatedAt=now,CreatedBy=user.UserId};
            db.StudentFines.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<StudentFineDto>.SuccessResponse(Map(row,enrollment.PublicId,rule.Name),
                "Fine recorded in register only; invoice/GL integration is pending.");
        }
        catch(DbUpdateConcurrencyException){return Error<StudentFineDto>("Fine changed; reload.",409);}
        catch(DbUpdateException){return Error<StudentFineDto>("Conflicting fine.",409);}
    }
    public async Task<ApiResponse<StudentFineDto>> WaiveFineAsync(long fineId, WaiveStudentFineRequestDto request, CancellationToken ct = default)
    {
        if(!Authorized) return Error<StudentFineDto>("Institution administrator permission required.",403);
        if(fineId<=0 || request==null || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length>1000) return Error<StudentFineDto>("Waiver reason is required.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var row=await StudentFines.FirstOrDefaultAsync(x=>x.Id==fineId,ct);
            if(row==null) return Error<StudentFineDto>("Fine not found.",404);
            if(!Matches(row.RowVersion,request.RowVersion)) return Error<StudentFineDto>("Fine changed; reload.",409);
            if(row.IsWaived) return Error<StudentFineDto>("Fine already waived.",409);
            if(await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x=>
                x.TenantId==user.TenantId && x.StudentFineId==row.Id,ct))
                return Error<StudentFineDto>("This fine has an invoice; use an audited credit-note/reversal workflow, not an administrative waiver.",409);
            var enrollment=await Enrollments.FirstOrDefaultAsync(x=>x.Id==row.StudentEnrollmentId,ct);
            var rule=await Fines.FirstOrDefaultAsync(x=>x.Id==row.FineRuleId,ct);
            if(enrollment==null || rule==null) return Error<StudentFineDto>("Fine references invalid tenant records.",409);
            row.IsWaived=true;row.WaivedByUserId=user.UserId;row.UpdatedAt=clock.GetUtcNow().UtcDateTime;row.UpdatedBy=user.UserId;
            // Persist the required reason separately; never overwrite the original fine reason.
            db.AuditLogs.Add(new AuditLog{TenantId=user.TenantId,UserId=user.UserId,Action="WaiveFine",
                EntityName="StudentFine",EntityId=row.Id,OldValue="Unwaived",NewValue=request.Reason.Trim(),
                OccurredAt=clock.GetUtcNow().UtcDateTime});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<StudentFineDto>.SuccessResponse(Map(row,enrollment.PublicId,rule.Name),
                "Fine waived with separate audit reason.");
        }
        catch(DbUpdateConcurrencyException){return Error<StudentFineDto>("Fine changed; reload.",409);}
        catch(DbUpdateException){return Error<StudentFineDto>("Conflicting fine waiver.",409);}
    }
    public async Task<ApiResponse<RefundDto>> RequestRefundAsync(CreateRefundRequestDto request, CancellationToken ct = default)
    {
        if(!Authorized) return Error<RefundDto>("Institution administrator permission required.",403);
        if(request==null || request.ClientRequestId==Guid.Empty || request.StudentPaymentReference==Guid.Empty ||
            request.Amount<=0 || !Scale(request.Amount,2) || request.Amount>999999999999m ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length>1000)
            return Error<RefundDto>("Invalid refund request.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var payment=await db.StudentPayments.FirstOrDefaultAsync(x=>x.TenantId==user.TenantId &&
                !x.IsDeleted && x.PublicId==request.StudentPaymentReference,ct);
            if(payment==null) return Error<RefundDto>("Payment not found.",404);
            if(payment.State!=PaymentState.Successful) return Error<RefundDto>("Only verified successful payments can be refunded.",409);
            if(request.Amount>payment.Amount) return Error<RefundDto>("Refund exceeds original payment.",409);
            var replay=await Refunds.FirstOrDefaultAsync(x=>x.ClientRequestId==request.ClientRequestId,ct);
            if(replay!=null)
            {
                if(replay.StudentPaymentId!=payment.Id || replay.Amount!=request.Amount || replay.Reason!=request.Reason.Trim())
                    return Error<RefundDto>("Idempotency key was reused with different refund details.",409);
                return ApiResponse<RefundDto>.SuccessResponse(Map(replay,payment.PublicId),"Existing refund request returned.");
            }
            var originalAllocations=await (from a in db.PaymentAllocations.AsNoTracking()
                join i in db.StudentInvoices.AsNoTracking()
                    on new { a.TenantId, Id=a.StudentInvoiceId } equals new { i.TenantId, i.Id }
                where a.TenantId==user.TenantId && a.StudentPaymentId==payment.Id &&
                    !a.IsDeleted && !i.IsDeleted
                select new { a.Amount,i.State,i.PaidAmount,i.TotalAmount,i.DueAmount }).ToListAsync(ct);
            if(originalAllocations.Count==0 || originalAllocations.Sum(x=>x.Amount)!=payment.Amount ||
                originalAllocations.Any(x=>!RefundInvoicePolicy.CanAdjust(x.State) ||
                    x.PaidAmount<=0m || x.PaidAmount+x.DueAmount!=x.TotalAmount))
                return Error<RefundDto>("Only cash receipts allocated to partially paid invoices can be refunded until the immutable paid-invoice credit-note integration is available.",409);
            var total=await Refunds.Where(x=>x.StudentPaymentId==payment.Id && x.State!=RefundState.Rejected && x.State!=RefundState.Cancelled)
                .SumAsync(x=>(decimal?)x.Amount,ct)??0m;
            if(total+request.Amount>payment.Amount) return Error<RefundDto>("Refund reservations exceed payment amount.",409);
            var pending=await Refunds.Where(x=>x.StudentPaymentId==payment.Id &&
                (x.State==RefundState.Draft || x.State==RefundState.Approved))
                .SumAsync(x=>(decimal?)x.Amount,ct)??0m;
            if(!await CanReserveRefundCapacityAsync(payment.Id,pending+request.Amount,ct))
                return Error<RefundDto>("Refund exceeds the remaining paid invoice capacity or pending reservations.",409);
            var now=clock.GetUtcNow().UtcDateTime;
            var row=new Refund{TenantId=user.TenantId,ClientRequestId=request.ClientRequestId,StudentPaymentId=payment.Id,
                Amount=request.Amount,Reason=request.Reason.Trim(),RequestedByUserId=user.UserId,
                CreatedAt=now,CreatedBy=user.UserId};
            db.Refunds.Add(row);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<RefundDto>.SuccessResponse(Map(row,payment.PublicId),"Refund request recorded; no payout performed.");
        }
        catch(DbUpdateConcurrencyException){return Error<RefundDto>("Refund changed; reload.",409);}
        catch(DbUpdateException){return Error<RefundDto>("Conflicting refund request.",409);}
    }



    public async Task<ApiResponse<IReadOnlyList<FinanceEnrollmentOptionDto>>> SearchEnrollmentsAsync(string search, CancellationToken ct = default)
    {
        if(!Authorized) return Error<IReadOnlyList<FinanceEnrollmentOptionDto>>("Institution administrator permission required.",403);
        if(string.IsNullOrWhiteSpace(search) || search.Trim().Length is <2 or >100)
            return Error<IReadOnlyList<FinanceEnrollmentOptionDto>>("Search requires 2–100 characters.");
        var term=search.Trim();
        var rows=await (from e in Enrollments.AsNoTracking()
            join student in db.Students.AsNoTracking() on new {e.TenantId,Id=e.StudentId} equals new {student.TenantId,Id=student.Id}
            where !student.IsDeleted && (student.StudentCode.StartsWith(term) || student.FullName.StartsWith(term))
            orderby student.StudentCode,e.Id descending
            select new FinanceEnrollmentOptionDto {Reference=e.PublicId,StudentCode=student.StudentCode,
                Name=student.FullName,CampusId=e.CampusId,RollNo=e.RollNo})
            .Take(50).ToListAsync(ct);
        return ApiResponse<IReadOnlyList<FinanceEnrollmentOptionDto>>.SuccessResponse(rows);
    }

    public async Task<ApiResponse<FinanceAdjustmentRegisterDto>> GetStudentRegisterAsync(Guid reference, CancellationToken ct = default)
    {
        if(!Authorized) return Error<FinanceAdjustmentRegisterDto>("Institution administrator permission required.",403);
        if(reference==Guid.Empty) return Error<FinanceAdjustmentRegisterDto>("Enrollment reference required.");
        var enrollment=await FindEnrollment(reference,ct);
        if(enrollment==null) return Error<FinanceAdjustmentRegisterDto>("Enrollment not found.",404);
        var discounts=await Assignments.AsNoTracking().Where(x=>x.StudentEnrollmentId==enrollment.Id)
            .OrderByDescending(x=>x.Id).Take(201).ToListAsync(ct);
        var fines=await StudentFines.AsNoTracking().Where(x=>x.StudentEnrollmentId==enrollment.Id)
            .OrderByDescending(x=>x.Id).Take(201).ToListAsync(ct);
        if(discounts.Count>200 || fines.Count>200) return Error<FinanceAdjustmentRegisterDto>("Register limit exceeded.",409);
        var ruleIds=discounts.Select(x=>x.DiscountRuleId).Distinct().ToArray();
        var fineIds=fines.Select(x=>x.FineRuleId).Distinct().ToArray();
        var studentFineIds=fines.Select(x=>x.Id).ToArray();
        var names=await Discounts.AsNoTracking().Where(x=>ruleIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        var fineNames=await Fines.AsNoTracking().Where(x=>fineIds.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.Name,ct);
        var linked=await (from line in db.StudentInvoiceLines.AsNoTracking()
            join invoice in db.StudentInvoices.AsNoTracking()
                on new {line.TenantId,Id=line.StudentInvoiceId} equals new {invoice.TenantId,invoice.Id}
            where line.TenantId==user.TenantId && line.StudentFineId.HasValue &&
                studentFineIds.Contains(line.StudentFineId.Value) && !line.IsDeleted && !invoice.IsDeleted &&
                invoice.JournalId.HasValue
            select new { FineId=line.StudentFineId!.Value,invoice.PublicId }).ToDictionaryAsync(x=>x.FineId,x=>x.PublicId,ct);
        return ApiResponse<FinanceAdjustmentRegisterDto>.SuccessResponse(new FinanceAdjustmentRegisterDto {
            Discounts=discounts.Select(x=>Map(x,reference,names.GetValueOrDefault(x.DiscountRuleId,"[deleted rule]"))).ToArray(),
            Fines=fines.Select(x=>Map(x,reference,fineNames.GetValueOrDefault(x.FineRuleId,"[deleted rule]"),
                linked.TryGetValue(x.Id,out var invoiceRef)?invoiceRef:null)).ToArray()
        });
    }
}
