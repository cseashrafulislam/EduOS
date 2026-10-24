using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Finance;

/// <summary>Only institutional posted cash receipts; partial refunds are contra journals, not destructive receipt reversals.</summary>
public sealed partial class FinanceAdjustmentService
{
    public async Task<ApiResponse<RefundOperationsRegisterDto>> GetRefundRegisterAsync(
        long? campusId, int page = 1, int pageSize = 25, CancellationToken ct = default)
    {
        if (!Authorized) return Error<RefundOperationsRegisterDto>("Institution administrator permission required.",403);
        if (campusId is <= 0 || page < 1 || pageSize is < 1 or > 50 ||
            (long)(page-1)*pageSize > int.MaxValue)
            return Error<RefundOperationsRegisterDto>("Invalid register filters.");
        var tenant=user.TenantId;
        var campuses=from a in db.PaymentAllocations.AsNoTracking()
            join i in db.StudentInvoices.AsNoTracking()
                on new { a.TenantId, Id=a.StudentInvoiceId } equals new { i.TenantId, i.Id }
            join e in db.StudentEnrollments.AsNoTracking()
                on new { i.TenantId, Id=i.StudentEnrollmentId } equals new { e.TenantId, e.Id }
            where a.TenantId==tenant && !a.IsDeleted && !i.IsDeleted && !e.IsDeleted
            group e.CampusId by a.StudentPaymentId into g
            select new { PaymentId=g.Key, CampusId=g.Min(), LastCampusId=g.Max() };
        var query=from p in db.StudentPayments.AsNoTracking()
            join c in campuses on p.Id equals c.PaymentId
            join s in db.Students.AsNoTracking()
                on new { p.TenantId, Id=p.StudentId } equals new { s.TenantId, s.Id }
            where p.TenantId==tenant && !p.IsDeleted && !s.IsDeleted &&
                p.State==PaymentState.Successful && p.PaymentMethod==PaymentMethodType.Cash &&
                p.CurrencyCode=="BDT" && p.JournalId.HasValue && c.CampusId==c.LastCampusId &&
                (!campusId.HasValue || c.CampusId==campusId.Value)
            select new { Payment=p, Student=s, c.CampusId };
        var count=await query.CountAsync(ct);
        var pageRows=await query.OrderByDescending(x=>x.Payment.PaymentDate).ThenByDescending(x=>x.Payment.Id)
            .Skip((page-1)*pageSize).Take(pageSize)
            .Select(x=>new { x.Payment.Id, x.Payment.PublicId, x.Payment.ReceiptNumber,
                x.Payment.Amount, x.CampusId, x.Student.StudentCode, x.Student.FullName }).ToListAsync(ct);
        var ids=pageRows.Select(x=>x.Id).ToArray();
        var refundRows=await Refunds.AsNoTracking().Where(x=>ids.Contains(x.StudentPaymentId))
            .OrderByDescending(x=>x.Id).Take(500).ToListAsync(ct);
        if (refundRows.Count==500) return Error<RefundOperationsRegisterDto>("Refund register limit reached; narrow the page.",409);
        var reserved=refundRows.Where(x=>x.State!=RefundState.Rejected && x.State!=RefundState.Cancelled)
            .GroupBy(x=>x.StudentPaymentId).ToDictionary(x=>x.Key,x=>x.Sum(r=>r.Amount));
        var byPayment=pageRows.ToDictionary(x=>x.Id);
        return ApiResponse<RefundOperationsRegisterDto>.SuccessResponse(new RefundOperationsRegisterDto
        {
            Page=page,PageSize=pageSize,TotalCount=count,
            Payments=pageRows.Select(x=>new RefundPaymentRegisterRowDto
            { PaymentReference=x.PublicId,ReceiptNumber=x.ReceiptNumber,Amount=x.Amount,
                CampusId=x.CampusId,StudentCode=x.StudentCode,StudentName=x.FullName,
                ReservedAmount=reserved.GetValueOrDefault(x.Id) }).ToArray(),
            Refunds=refundRows.Select(x=>new RefundRegisterRowDto
            { Id=x.Id,StudentPaymentReference=byPayment[x.StudentPaymentId].PublicId,
                ReceiptNumber=byPayment[x.StudentPaymentId].ReceiptNumber,
                Amount=x.Amount,State=x.State,Reason=x.Reason,
                RequestedByUserId=x.RequestedByUserId,ApprovedByUserId=x.ApprovedByUserId,
                ApprovedAt=x.ApprovedAt,PaidAt=x.PaidAt,ReversalJournalId=x.ReversalJournalId,
                RowVersion=Version(x.RowVersion) }).ToArray()
        });
    }

    public async Task<ApiResponse<RefundDto>> ReviewRefundAsync(long refundId,
        ReviewRefundRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<RefundDto>("Institution administrator permission required.",403);
        if (refundId<=0 || request==null || string.IsNullOrWhiteSpace(request.RowVersion) ||
            request.RowVersion.Length>256 || request.Note?.Length>1000 ||
            (!request.Approve && string.IsNullOrWhiteSpace(request.Note)))
            return Error<RefundDto>("Refund review requires valid row version and rejection reason.");
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var refund=await Refunds.FirstOrDefaultAsync(x=>x.Id==refundId,ct);
            if(refund==null) return Error<RefundDto>("Refund not found.",404);
            if(refund.State!=RefundState.Draft || !Matches(refund.RowVersion,request.RowVersion))
                return Error<RefundDto>("Refund changed or already reviewed.",409);
            if(refund.RequestedByUserId==user.UserId)
                return Error<RefundDto>("Requester cannot review their own refund.",403);
            var payment=await db.StudentPayments.AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==user.TenantId && x.Id==refund.StudentPaymentId && !x.IsDeleted,ct);
            if(payment==null) return Error<RefundDto>("Original payment missing.",409);
            if(request.Approve)
            {
                if(payment.State!=PaymentState.Successful || payment.PaymentMethod!=PaymentMethodType.Cash ||
                    payment.CurrencyCode!="BDT" || !payment.JournalId.HasValue)
                    return Error<RefundDto>("Only posted institutional BDT cash receipts can be approved.",409);
                var receipt=await db.Journals.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==user.TenantId &&
                    x.Id==payment.JournalId.Value && !x.IsDeleted,ct);
                if(receipt==null || receipt.State!=JournalState.Posted ||
                    receipt.SourceType!="StudentPayment" || receipt.SourceId!=payment.Id)
                    return Error<RefundDto>("Original receipt journal is not posted or lacks provenance.",409);
                var reserved=await Refunds.Where(x=>x.StudentPaymentId==payment.Id &&
                    x.State!=RefundState.Rejected && x.State!=RefundState.Cancelled)
                    .SumAsync(x=>(decimal?)x.Amount,ct)??0m;
                if(reserved>payment.Amount) return Error<RefundDto>("Refund reservations exceed original payment.",409);
                var allocatedInvoices=await (from a in db.PaymentAllocations.AsNoTracking()
                    join i in db.StudentInvoices.AsNoTracking()
                        on new { a.TenantId, Id=a.StudentInvoiceId } equals new { i.TenantId, i.Id }
                    where a.TenantId==user.TenantId && a.StudentPaymentId==payment.Id &&
                        !a.IsDeleted && !i.IsDeleted
                    select new { i.State,i.PaidAmount,i.TotalAmount,i.DueAmount }).ToListAsync(ct);
                if(allocatedInvoices.Count==0 || allocatedInvoices.Any(x=>
                    !RefundInvoicePolicy.CanAdjust(x.State) || x.PaidAmount<=0m ||
                    x.PaidAmount+x.DueAmount!=x.TotalAmount))
                    return Error<RefundDto>("Refund of fully paid invoices requires a Platform-owned immutable invoice credit-note integration; only partially paid invoices are currently supported.",409);
            }
            var now=clock.GetUtcNow().UtcDateTime;
            refund.State=request.Approve?RefundState.Approved:RefundState.Rejected;
            refund.ApprovedByUserId=request.Approve?user.UserId:null;
            refund.ApprovedAt=request.Approve?now:null;
            refund.UpdatedAt=now;refund.UpdatedBy=user.UserId;
            db.AuditLogs.Add(new AuditLog{TenantId=user.TenantId,UserId=user.UserId,
                Action=request.Approve?"ApproveStudentRefund":"RejectStudentRefund",EntityName="Refund",
                EntityId=refund.Id,OldValue="Draft",NewValue=request.Note?.Trim()??"Approved",
                OccurredAt=now});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<RefundDto>.SuccessResponse(Map(refund,payment.PublicId),
                request.Approve?"Refund approved; no money disbursed.":"Refund rejected.");
        }
        catch(DbUpdateConcurrencyException){return Error<RefundDto>("Refund changed; reload.",409);}
        catch(DbUpdateException){return Error<RefundDto>("Conflicting refund review.",409);}
    }

    private static Guid RefundPostingKey(long tenant,long refundId)
    {
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes("EduOS:Institution:StudentRefund:"+tenant+":"+refundId));
        return new Guid(hash.AsSpan(0,16));
    }
    private static bool ValidMoney(decimal value) => value>0m && value<=999999999999m &&
        decimal.Round(value,2)==value;

    public async Task<ApiResponse<RefundDto>> RecordRefundDisbursementAsync(long refundId,
        RecordRefundDisbursementRequestDto request, CancellationToken ct = default)
    {
        if(!Authorized) return Error<RefundDto>("Institution administrator permission required.",403);
        if(refundId<=0 || request==null || request.ClientRequestId==Guid.Empty ||
            request.AccountingPeriodId<=0 || request.PayoutDate==default ||
            string.IsNullOrWhiteSpace(request.PayoutEvidenceReference) ||
            request.PayoutEvidenceReference.Trim().Length>100 ||
            string.IsNullOrWhiteSpace(request.RowVersion) || request.RowVersion.Length>256)
            return Error<RefundDto>("Payout date, open period, independent voucher and row version required.");
        var tenant=user.TenantId;
        var evidence=request.PayoutEvidenceReference.Trim();
        try
        {
            await using var tx=await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct);
            var refund=await Refunds.FirstOrDefaultAsync(x=>x.Id==refundId,ct);
            if(refund==null) return Error<RefundDto>("Refund not found.",404);
            var payment=await db.StudentPayments.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==tenant &&
                x.Id==refund.StudentPaymentId && !x.IsDeleted,ct);
            if(payment==null) return Error<RefundDto>("Original payment missing.",409);
            if(refund.State==RefundState.Paid)
            {
                var previous=await db.Journals.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==tenant &&
                    x.Id==refund.ReversalJournalId && !x.IsDeleted,ct);
                if(previous==null || previous.State!=JournalState.Posted ||
                    previous.SourceType!="StudentRefund" || previous.SourceId!=refund.Id ||
                    previous.ReferenceNumber!=evidence || previous.JournalDate!=request.PayoutDate ||
                    previous.AccountingPeriodId!=request.AccountingPeriodId)
                    return Error<RefundDto>("Prior payout requires reconciliation; replay details differ.",409);
                return ApiResponse<RefundDto>.SuccessResponse(Map(refund,payment.PublicId),"Existing posted refund returned.");
            }
            if(refund.State!=RefundState.Approved || !Matches(refund.RowVersion,request.RowVersion) ||
                !refund.ApprovedByUserId.HasValue || refund.RequestedByUserId==user.UserId ||
                !ValidMoney(refund.Amount))
                return Error<RefundDto>("Refund is not independently approved or has changed.",409);
            if(payment.State!=PaymentState.Successful || payment.PaymentMethod!=PaymentMethodType.Cash ||
                payment.BankAccountId.HasValue || payment.CurrencyCode!="BDT" ||
                !payment.JournalId.HasValue || request.PayoutDate<payment.PaymentDate ||
                request.PayoutDate>DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime.AddDays(1)))
                return Error<RefundDto>("Only actual BDT cash refunds of posted receipts are supported.",409);
            var period=await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x=>
                x.TenantId==tenant && x.Id==request.AccountingPeriodId && !x.IsDeleted &&
                !x.IsClosed && x.StartDate<=request.PayoutDate && x.EndDate>=request.PayoutDate,ct);
            if(period==null) return Error<RefundDto>("Open period must cover actual payout date.",409);
            if(await db.Journals.AsNoTracking().AnyAsync(x=>x.TenantId==tenant && !x.IsDeleted &&
                ((x.SourceType=="StudentRefund" && x.SourceId==refund.Id) ||
                 x.IdempotencyKey==RefundPostingKey(tenant,refund.Id)),ct) ||
               await db.RefundAllocations.AsNoTracking().AnyAsync(x=>x.TenantId==tenant &&
                   x.RefundId==refund.Id && !x.IsDeleted,ct))
                return Error<RefundDto>("Unlinked refund posting exists; reconcile before retry.",409);
            var receipt=await db.Journals.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==tenant &&
                x.Id==payment.JournalId.Value && !x.IsDeleted,ct);
            if(receipt==null || receipt.State!=JournalState.Posted ||
                receipt.SourceType!="StudentPayment" || receipt.SourceId!=payment.Id ||
                receipt.ReversalOfJournalId.HasValue || receipt.CurrencyCode!="BDT" ||
                !receipt.CampusId.HasValue ||
                await db.Journals.AsNoTracking().AnyAsync(x=>x.TenantId==tenant && !x.IsDeleted &&
                    x.State==JournalState.Posted && x.ReversalOfJournalId==receipt.Id,ct))
                return Error<RefundDto>("Original receipt journal is missing or reversed.",409);
            var receiptLines=await db.JournalLines.AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.JournalId==receipt.Id && !x.IsDeleted).ToListAsync(ct);
            var cashDebits=receiptLines.Where(x=>x.DebitAmount>0m).ToArray();
            var receiptCredits=receiptLines.Where(x=>x.CreditAmount>0m).ToArray();
            if(cashDebits.Length!=1 || receiptCredits.Length==0 ||
                cashDebits[0].DebitAmount!=payment.Amount ||
                receiptCredits.Sum(x=>x.CreditAmount)!=payment.Amount ||
                receiptLines.Any(x=>x.StudentId!=payment.StudentId || x.CampusId!=receipt.CampusId))
                return Error<RefundDto>("Receipt journal is not a balanced cash-to-receivables posting.",409);
            var cashId=cashDebits[0].AccountId;
            var cash=await db.Accounts.AsNoTracking().FirstOrDefaultAsync(x=>x.TenantId==tenant &&
                x.Id==cashId && !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting &&
                x.AccountType==AccountType.Asset && x.CurrencyCode=="BDT",ct);
            if(cash==null || !(cash.Code.Contains("cash",StringComparison.OrdinalIgnoreCase) ||
                cash.Name.Contains("cash",StringComparison.OrdinalIgnoreCase)) ||
                await db.BankAccounts.AsNoTracking().AnyAsync(x=>x.TenantId==tenant &&
                    x.AccountId==cashId && !x.IsDeleted,ct))
                return Error<RefundDto>("Original cash-in-hand ledger is invalid.",409);
            var allocations=await db.PaymentAllocations.AsNoTracking().Where(x=>x.TenantId==tenant &&
                x.StudentPaymentId==payment.Id && !x.IsDeleted).OrderBy(x=>x.Id).ToListAsync(ct);
            if(allocations.Count is <1 or >99 || allocations.Any(x=>!ValidMoney(x.Amount)) ||
                allocations.Select(x=>x.StudentInvoiceId).Distinct().Count()!=allocations.Count ||
                allocations.Sum(x=>x.Amount)!=payment.Amount)
                return Error<RefundDto>("Original payment allocations do not reconcile.",409);
            var allocationIds=allocations.Select(x=>x.Id).ToArray();
            var prior=await (from ra in db.RefundAllocations.AsNoTracking()
                join r in db.Refunds.AsNoTracking()
                    on new { ra.TenantId, Id=ra.RefundId } equals new { r.TenantId, r.Id }
                where ra.TenantId==tenant && !ra.IsDeleted && !r.IsDeleted &&
                    r.State==RefundState.Paid && allocationIds.Contains(ra.PaymentAllocationId)
                select new { ra.PaymentAllocationId, ra.Amount }).ToListAsync(ct);
            var priorTotals=prior.GroupBy(x=>x.PaymentAllocationId)
                .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Amount));
            IReadOnlyList<RefundAllocationPortion> portions;
            try
            {
                portions=RefundAllocationPlanner.Plan(allocations.Select(x=>
                    new RefundAllocationBalance(x.Id,x.Amount,priorTotals.GetValueOrDefault(x.Id))).ToArray(),refund.Amount);
            }
            catch(InvalidOperationException){return Error<RefundDto>("Refund exceeds unreversed original allocations.",409);}
            var invoiceIds=allocations.Select(x=>x.StudentInvoiceId).ToArray();
            var invoices=await db.StudentInvoices.Where(x=>x.TenantId==tenant &&
                invoiceIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
            if(invoices.Count!=invoiceIds.Length) return Error<RefundDto>("Invoice allocation missing.",409);
            var enrollmentIds=invoices.Select(x=>x.StudentEnrollmentId).Distinct().ToArray();
            var enrollments=await db.StudentEnrollments.AsNoTracking().Where(x=>x.TenantId==tenant &&
                enrollmentIds.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
            if(enrollments.Count!=enrollmentIds.Length || enrollments.Any(x=>
                x.CampusId!=receipt.CampusId || x.StudentId!=payment.StudentId))
                return Error<RefundDto>("Cross-campus or cross-student refunds are prohibited.",409);
            var invoiceJournals=invoices.Where(x=>x.JournalId.HasValue).Select(x=>x.JournalId!.Value).Distinct().ToArray();
            if(invoiceJournals.Length!=invoices.Count) return Error<RefundDto>("Invoice journals missing or shared.",409);
            var sourceJournals=await db.Journals.AsNoTracking().Where(x=>x.TenantId==tenant &&
                invoiceJournals.Contains(x.Id) && !x.IsDeleted).ToListAsync(ct);
            if(sourceJournals.Count!=invoiceJournals.Length ||
                await db.Journals.AsNoTracking().AnyAsync(x=>x.TenantId==tenant && !x.IsDeleted &&
                    x.State==JournalState.Posted && x.ReversalOfJournalId.HasValue &&
                    invoiceJournals.Contains(x.ReversalOfJournalId.Value),ct))
                return Error<RefundDto>("Invoice journal missing or reversed.",409);
            var sourceLines=await db.JournalLines.AsNoTracking().Where(x=>x.TenantId==tenant &&
                invoiceJournals.Contains(x.JournalId) && !x.IsDeleted).ToListAsync(ct);
            var receivables=new Dictionary<long,long>();
            foreach(var allocation in allocations)
            {
                var invoice=invoices.Single(x=>x.Id==allocation.StudentInvoiceId);
                var source=sourceJournals.Single(x=>x.Id==invoice.JournalId);
                var invoiceLines=sourceLines.Where(x=>x.JournalId==source.Id).ToArray();
                var debits=invoiceLines.Where(x=>x.DebitAmount>0m).ToArray();
                if(source.State!=JournalState.Posted || source.SourceType!="StudentInvoice" ||
                    source.SourceId!=invoice.Id || source.CampusId!=receipt.CampusId ||
                    source.ReversalOfJournalId.HasValue || invoice.CurrencyCode!="BDT" ||
                    !RefundInvoicePolicy.CanAdjust(invoice.State) ||
                    invoice.TotalAmount<=0m || invoice.PaidAmount<0m ||
                    invoice.DueAmount<0m || invoice.PaidAmount+invoice.DueAmount!=invoice.TotalAmount ||
                    debits.Length==0 || debits.Select(x=>x.AccountId).Distinct().Count()!=1 ||
                    invoiceLines.Sum(x=>x.DebitAmount)!=invoice.TotalAmount ||
                    invoiceLines.Sum(x=>x.CreditAmount)!=invoice.TotalAmount ||
                    debits.Any(x=>x.StudentId!=payment.StudentId || x.CampusId!=receipt.CampusId))
                    return Error<RefundDto>("Invoice GL provenance or receivable ledger is inconsistent.",409);
                receivables.Add(allocation.Id,debits[0].AccountId);
            }
            var originalCredits=allocations.GroupBy(x=>receivables[x.Id])
                .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.Amount));
            var actualCredits=receiptCredits.GroupBy(x=>x.AccountId)
                .ToDictionary(x=>x.Key,x=>x.Sum(y=>y.CreditAmount));
            if(originalCredits.Count!=actualCredits.Count ||
                originalCredits.Any(x=>!actualCredits.TryGetValue(x.Key,out var amount)||amount!=x.Value))
                return Error<RefundDto>("Receipt receivables do not match original invoice allocations.",409);
            var accountIds=receivables.Values.Distinct().ToArray();
            var validAccounts=await db.Accounts.AsNoTracking().Where(x=>x.TenantId==tenant &&
                accountIds.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                x.AllowsPosting && x.CurrencyCode=="BDT" && x.AccountType==AccountType.Asset)
                .Select(x=>x.Id).ToListAsync(ct);
            if(validAccounts.Count!=accountIds.Length || accountIds.Contains(cashId))
                return Error<RefundDto>("Receivable account is invalid or equals cash ledger.",409);
            foreach(var portion in portions)
            {
                var allocation=allocations.Single(x=>x.Id==portion.AllocationId);
                var invoice=invoices.Single(x=>x.Id==allocation.StudentInvoiceId);
                if(invoice.PaidAmount<portion.Amount)
                    return Error<RefundDto>("Invoice paid balance cannot cover refund.",409);
            }
            var now=clock.GetUtcNow().UtcDateTime;
            var guid=Guid.NewGuid();
            var journal=new Journal{TenantId=tenant,PublicId=guid,
                IdempotencyKey=RefundPostingKey(tenant,refund.Id),
                JournalNumber="JRN-"+guid.ToString("N").ToUpperInvariant(),
                JournalDate=request.PayoutDate,AccountingPeriodId=period.Id,CampusId=receipt.CampusId,
                ReferenceNumber=evidence,CurrencyCode="BDT",State=JournalState.Posted,
                SourceType="StudentRefund",SourceId=refund.Id,
                Description="Verified cash refund of receipt "+payment.ReceiptNumber,
                ApprovedByUserId=refund.ApprovedByUserId,ApprovedAt=refund.ApprovedAt,
                PostedByUserId=user.UserId,PostedAt=now,CreatedAt=now,CreatedBy=user.UserId};
            db.Journals.Add(journal);await db.SaveChangesAsync(ct);
            var grouped=portions.GroupBy(x=>receivables[x.AllocationId])
                .Select(x=>new { AccountId=x.Key,Amount=x.Sum(y=>y.Amount) }).OrderBy(x=>x.AccountId).ToArray();
            if(grouped.Sum(x=>x.Amount)!=refund.Amount || grouped.Length>99)
                return Error<RefundDto>("Refund journal does not balance.",409);
            var lines=new List<JournalLine>();
            var lineNo=0;
            foreach(var item in grouped)
                lines.Add(new JournalLine{TenantId=tenant,JournalId=journal.Id,LineNo=++lineNo,
                    AccountId=item.AccountId,CampusId=receipt.CampusId,StudentId=payment.StudentId,
                    DebitAmount=item.Amount,Description="Restore student receivable after cash refund",
                    CreatedAt=now,CreatedBy=user.UserId});
            lines.Add(new JournalLine{TenantId=tenant,JournalId=journal.Id,LineNo=++lineNo,
                AccountId=cashId,CampusId=receipt.CampusId,StudentId=payment.StudentId,
                CreditAmount=refund.Amount,Description="Verified cash refund payout",
                CreatedAt=now,CreatedBy=user.UserId});
            db.JournalLines.AddRange(lines);
            foreach(var portion in portions)
            {
                var allocation=allocations.Single(x=>x.Id==portion.AllocationId);
                var invoice=invoices.Single(x=>x.Id==allocation.StudentInvoiceId);
                invoice.PaidAmount-=portion.Amount;invoice.DueAmount+=portion.Amount;
                invoice.State=invoice.PaidAmount==0m?InvoiceState.Issued:InvoiceState.PartiallyPaid;
                invoice.PaidAt=null;invoice.UpdatedAt=now;invoice.UpdatedBy=user.UserId;
                db.RefundAllocations.Add(new RefundAllocation{TenantId=tenant,RefundId=refund.Id,
                    PaymentAllocationId=allocation.Id,Amount=portion.Amount,CreatedAt=now,CreatedBy=user.UserId});
            }
            refund.State=RefundState.Paid;refund.PaidAt=now;refund.ReversalJournalId=journal.Id;
            refund.UpdatedAt=now;refund.UpdatedBy=user.UserId;
            db.AuditLogs.Add(new AuditLog{TenantId=tenant,UserId=user.UserId,Action="RecordVerifiedCashRefund",
                EntityName="Refund",EntityId=refund.Id,OldValue="Approved",
                NewValue="Cash voucher: "+evidence+"; Payout date: "+request.PayoutDate+
                    "; Request: "+request.ClientRequestId,
                OccurredAt=now});
            await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
            return ApiResponse<RefundDto>.SuccessResponse(Map(refund,payment.PublicId),
                "Verified payout recorded; balanced institutional contra journal posted.");
        }
        catch(OverflowException){return Error<RefundDto>("Refund exceeds ledger precision.",409);}
        catch(DbUpdateConcurrencyException){return Error<RefundDto>("Refund or invoice changed; reload.",409);}
        catch(DbUpdateException){return Error<RefundDto>("Refund posting conflict; refresh and retry.",409);}
    }
}

/// <summary>Deterministic distribution of a refund across the remaining original immutable payment allocations.</summary>
public sealed record RefundAllocationBalance(long AllocationId,decimal OriginalAmount,decimal RefundedAmount);
public sealed record RefundAllocationPortion(long AllocationId,decimal Amount);
public static class RefundAllocationPlanner
{
    public static IReadOnlyList<RefundAllocationPortion> Plan(
        IReadOnlyList<RefundAllocationBalance> sources,decimal amount)
    {
        if(amount<=0m || decimal.Round(amount,2)!=amount || sources.Count is <1 or >99 ||
            sources.Select(x=>x.AllocationId).Distinct().Count()!=sources.Count ||
            sources.Any(x=>x.AllocationId<=0 || x.OriginalAmount<=0m ||
                decimal.Round(x.OriginalAmount,2)!=x.OriginalAmount ||
                x.RefundedAmount<0m || decimal.Round(x.RefundedAmount,2)!=x.RefundedAmount ||
                x.RefundedAmount>x.OriginalAmount))
            throw new InvalidOperationException("Invalid original or refunded allocation.");
        var left=amount;
        var result=new List<RefundAllocationPortion>();
        foreach(var source in sources.OrderBy(x=>x.AllocationId))
        {
            var take=Math.Min(left,source.OriginalAmount-source.RefundedAmount);
            if(take>0m){result.Add(new RefundAllocationPortion(source.AllocationId,take));left-=take;}
            if(left==0m)break;
        }
        if(left!=0m)throw new InvalidOperationException("Refund exceeds available allocation.");
        return result;
    }
}

/// <summary>Paid invoices are immutable in the shared DbContext. Never mutate them from Finance.</summary>
public static class RefundInvoicePolicy
{
    public static bool CanAdjust(InvoiceState state) => state==InvoiceState.PartiallyPaid;
}
