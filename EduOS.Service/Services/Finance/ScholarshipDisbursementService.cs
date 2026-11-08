using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Platform;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace EduOS.Service.Services.Finance;

/// <summary>Records institution-confirmed, external cash/bank scholarship payments in the
/// canonical Finance GL. Never instructs a bank or transfers money.</summary>
public sealed class ScholarshipDisbursementService(EduOSDbContext db, ICurrentUserService user)
{
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private async Task<bool> FinanceAdmin(CancellationToken ct) => user.IsTenantAdmin &&
        await CentralPrivilegedAccess.HasRoleAsync(db, user, new[] { "TENANTADMIN" }, ct);
    private async Task<DateOnly?> InstitutionToday(CancellationToken ct)
    {
        var zoneId = await db.Tenants.AsNoTracking().Where(x => x.Id == user.TenantId && !x.IsDeleted)
            .Select(x => x.TimeZoneId).FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(zoneId)) return null;
        try
        {
            var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,
                TimeZoneInfo.FindSystemTimeZoneById(zoneId));
            return DateOnly.FromDateTime(today);
        }
        catch (TimeZoneNotFoundException) { return null; }
        catch (InvalidTimeZoneException) { return null; }
    }
    private static bool Version(byte[] actual, string? version)
    {
        try { return !string.IsNullOrEmpty(version) && actual.AsSpan().SequenceEqual(Convert.FromBase64String(version)); }
        catch (FormatException) { return false; }
    }
    private static ScholarshipDisbursementRow Map(ScholarshipDisbursement x, Guid applicationReference,
        Guid? journalReference = null, Guid? reversalReference = null) => new()
    {
        Reference = x.PublicId, ApplicationReference = applicationReference,
        State = x.State, Amount = x.Amount, Method = x.Method,
        BeneficiaryType = x.BeneficiaryType, BeneficiaryName = x.BeneficiaryNameSnapshot,
        PaymentDate = x.PaymentDate, ExternalPaymentReference = x.ExternalPaymentReference,
        JournalReference = journalReference, ReversalJournalReference = reversalReference,
        RowVersion = Convert.ToBase64String(x.RowVersion)
    };
    private async Task<ScholarshipDisbursementRow> Detail(ScholarshipDisbursement row,
        Guid applicationReference, CancellationToken ct)
    {
        var journalIds = new[] { row.JournalId, row.ReversalJournalId }.Where(x => x.HasValue)
            .Select(x => x!.Value).ToArray();
        var journals = await db.Journals.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && journalIds.Contains(x.Id) && !x.IsDeleted)
            .Select(x => new { x.Id, x.PublicId }).ToArrayAsync(ct);
        return Map(row, applicationReference, journals.FirstOrDefault(x => x.Id == row.JournalId)?.PublicId,
            journals.FirstOrDefault(x => x.Id == row.ReversalJournalId)?.PublicId);
    }

    public async Task<ApiResponse<IReadOnlyList<ScholarshipDisbursementPayee>>> PayeesAsync(
        Guid applicationReference, CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<IReadOnlyList<ScholarshipDisbursementPayee>>(
            "Finance administrator required.", 403);
        var source = await (from aid in db.ScholarshipApplications.AsNoTracking()
            join enrollment in db.StudentEnrollments.AsNoTracking()
                on new { aid.TenantId, Id = aid.StudentEnrollmentId }
                equals new { enrollment.TenantId, enrollment.Id }
            join student in db.Students.AsNoTracking()
                on new { enrollment.TenantId, Id = enrollment.StudentId }
                equals new { student.TenantId, student.Id }
            where aid.TenantId == user.TenantId && aid.PublicId == applicationReference &&
                aid.AwardMode == "CashGrant" && !aid.IsDeleted &&
                !enrollment.IsDeleted && !student.IsDeleted
            select new { enrollment.StudentId, student.FullName }).FirstOrDefaultAsync(ct);
        if (source == null) return Fail<IReadOnlyList<ScholarshipDisbursementPayee>>(
            "Cash grant unavailable.", 404);
        var payees = new List<ScholarshipDisbursementPayee> { new() { Name = source.FullName } };
        var guardians = await (from link in db.StudentGuardians.AsNoTracking()
            join g in db.Guardians.AsNoTracking()
                on new { link.TenantId, Id = link.GuardianId } equals new { g.TenantId, g.Id }
            where link.TenantId == user.TenantId && link.StudentId == source.StudentId &&
                link.IsFinancialContact && !link.IsDeleted && g.IsActive && !g.IsDeleted
            orderby g.Id
            select new { g.PublicId, g.FullName }).Take(30).ToArrayAsync(ct);
        payees.AddRange(guardians.Select(g => new ScholarshipDisbursementPayee
        { GuardianReference = g.PublicId, Name = g.FullName, Type = "Guardian" }));
        return ApiResponse<IReadOnlyList<ScholarshipDisbursementPayee>>.SuccessResponse(payees);
    }

    public async Task<ApiResponse<ScholarshipDisbursementRow>> GetAsync(Guid applicationReference,
        CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<ScholarshipDisbursementRow>("Finance administrator required.", 403);
        var source = await (from payment in db.ScholarshipDisbursements.AsNoTracking()
            join aid in db.ScholarshipApplications.AsNoTracking()
                on new { payment.TenantId, Id = payment.ScholarshipApplicationId }
                equals new { aid.TenantId, aid.Id }
            where payment.TenantId == user.TenantId && aid.PublicId == applicationReference &&
                !payment.IsDeleted && !aid.IsDeleted
            select new { payment, aid.PublicId }).OrderByDescending(x => x.payment.Id)
            .FirstOrDefaultAsync(ct);
        if (source == null) return Fail<ScholarshipDisbursementRow>("Cash disbursement not found.", 404);
        return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
            await Detail(source.payment, source.PublicId, ct));
    }

    private async Task<bool> ValidAccounts(ScholarshipDisbursement payment, long campusId,
        CancellationToken ct)
    {
        if (payment.ExpenseAccountId == payment.PaymentAccountId) return false;
        var accounts = await db.Accounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.IsActive && !x.IsGroup &&
            x.AllowsPosting && x.CurrencyCode == "BDT" &&
            (x.Id == payment.ExpenseAccountId || x.Id == payment.PaymentAccountId))
            .ToListAsync(ct);
        var expense = accounts.SingleOrDefault(x => x.Id == payment.ExpenseAccountId);
        var paymentAccount = accounts.SingleOrDefault(x => x.Id == payment.PaymentAccountId);
        if (expense?.AccountType != AccountType.Expense || paymentAccount?.AccountType != AccountType.Asset)
            return false;
        var bank = await db.BankAccounts.AsNoTracking().Where(x =>
            x.TenantId == user.TenantId && !x.IsDeleted && x.AccountId == payment.PaymentAccountId)
            .ToArrayAsync(ct);
        if (payment.Method == "Cash")
            return paymentAccount.IsCashInHand && bank.Length == 0 && !payment.BankAccountId.HasValue;
        if (payment.Method != "Bank" || paymentAccount.IsCashInHand || !payment.BankAccountId.HasValue)
            return false;
        return bank.Length == 1 && bank[0].Id == payment.BankAccountId && bank[0].IsActive &&
            bank[0].CurrencyCode == "BDT" && (!bank[0].CampusId.HasValue || bank[0].CampusId == campusId);
    }

    public async Task<ApiResponse<ScholarshipDisbursementRow>> PrepareAsync(Guid applicationReference,
        PrepareScholarshipDisbursementRequest request, CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<ScholarshipDisbursementRow>("Finance administrator required.", 403);
        if (applicationReference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.Method is not ("Cash" or "Bank") || request.ExpenseAccountId <= 0 ||
            request.PaymentAccountId <= 0 || request.AccountingPeriodId <= 0 ||
            request.PaymentDate.Year is < 2000 or > 2100 ||
            string.IsNullOrWhiteSpace(request.ExternalPaymentReference) ||
            request.ExternalPaymentReference.Trim().Length > 100 ||
            (request.Method == "Cash" && request.BankAccountId.HasValue) ||
            (request.Method == "Bank" && (!request.BankAccountId.HasValue || request.BankAccountId <= 0)))
            return Fail<ScholarshipDisbursementRow>("Valid method, payee, account, period, date and receipt reference required.");
        var receipt = request.ExternalPaymentReference.Trim();
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await (from application in db.ScholarshipApplications
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { application.TenantId, Id = application.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                join student in db.Students.AsNoTracking()
                    on new { enrollment.TenantId, Id = enrollment.StudentId }
                    equals new { student.TenantId, student.Id }
                where application.TenantId == user.TenantId && application.PublicId == applicationReference &&
                    !application.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted
                select new { Aid = application, enrollment.StudentId, enrollment.CampusId, student.FullName })
                .SingleOrDefaultAsync(ct);
            if (source == null) return Fail<ScholarshipDisbursementRow>("Scholarship unavailable.", 404);
            var aid = source.Aid;
            var existing = await db.ScholarshipDisbursements.FirstOrDefaultAsync(x =>
                x.TenantId == user.TenantId && x.ScholarshipApplicationId == aid.Id &&
                x.State != "Rejected", ct);
            if (existing != null)
            {
                if (existing.ClientRequestId != request.RequestId || existing.PreparedByUserId != user.UserId ||
                    existing.Method != request.Method || existing.ExpenseAccountId != request.ExpenseAccountId ||
                    existing.PaymentAccountId != request.PaymentAccountId ||
                    existing.BankAccountId != request.BankAccountId ||
                    existing.AccountingPeriodId != request.AccountingPeriodId ||
                    existing.PaymentDate != request.PaymentDate || existing.ExternalPaymentReference != receipt)
                    return Fail<ScholarshipDisbursementRow>("Cash award already has a disbursement instruction.", 409);
                if (request.GuardianReference.HasValue != existing.GuardianId.HasValue ||
                    (request.GuardianReference.HasValue && !await db.Guardians.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == existing.GuardianId &&
                        x.PublicId == request.GuardianReference, ct)))
                    return Fail<ScholarshipDisbursementRow>("Request key reused for a different beneficiary.", 409);
                return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
                    await Detail(existing, aid.PublicId, ct));
            }
            if (aid.AwardMode != "CashGrant" || aid.State != "Approved" ||
                !aid.CashAmount.HasValue || aid.CashAmount <= 0m || aid.DiscountRuleId.HasValue ||
                aid.StudentDiscountId.HasValue || aid.ReviewedByUserId == null ||
                aid.SubmittedByUserId == user.UserId || aid.ReviewedByUserId == aid.SubmittedByUserId ||
                !Version(aid.RowVersion, request.RowVersion) ||
                request.PaymentDate < aid.EffectiveFrom ||
                (aid.EffectiveTo.HasValue && request.PaymentDate > aid.EffectiveTo))
                return Fail<ScholarshipDisbursementRow>("Only independently approved, uncredited cash aid may be paid.", 409);
            if (!await db.Campuses.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == source.CampusId && x.IsActive && !x.IsDeleted, ct) ||
                !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= request.PaymentDate && x.EndDate >= request.PaymentDate, ct))
                return Fail<ScholarshipDisbursementRow>("Active campus and open accounting period required.", 409);
            long? guardianId = null;
            var beneficiaryName = source.FullName;
            if (request.GuardianReference.HasValue)
            {
                var guardian = await (from link in db.StudentGuardians.AsNoTracking()
                    join person in db.Guardians.AsNoTracking()
                        on new { link.TenantId, Id = link.GuardianId }
                        equals new { person.TenantId, person.Id }
                    where link.TenantId == user.TenantId && link.StudentId == source.StudentId &&
                        !link.IsDeleted && !person.IsDeleted && person.IsActive &&
                        link.IsFinancialContact && person.PublicId == request.GuardianReference.Value
                    select new { person.Id, person.FullName }).FirstOrDefaultAsync(ct);
                if (guardian == null) return Fail<ScholarshipDisbursementRow>("Verified financial guardian required.", 409);
                guardianId = guardian.Id; beneficiaryName = guardian.FullName;
            }
            if (string.IsNullOrWhiteSpace(beneficiaryName))
                return Fail<ScholarshipDisbursementRow>("Verified beneficiary required.", 409);
            if (await db.ScholarshipDisbursements.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, ct))
                return Fail<ScholarshipDisbursementRow>("Disbursement request key already used.", 409);
            var now = DateTime.UtcNow;
            var instruction = new ScholarshipDisbursement
            {
                TenantId = user.TenantId, ScholarshipApplicationId = aid.Id,
                ClientRequestId = request.RequestId, Amount = aid.CashAmount.Value,
                Method = request.Method, BeneficiaryType = guardianId.HasValue ? "Guardian" : "Student",
                GuardianId = guardianId, BeneficiaryNameSnapshot = beneficiaryName.Trim(),
                ExpenseAccountId = request.ExpenseAccountId, PaymentAccountId = request.PaymentAccountId,
                BankAccountId = request.BankAccountId, AccountingPeriodId = request.AccountingPeriodId,
                PaymentDate = request.PaymentDate, ExternalPaymentReference = receipt,
                PreparedByUserId = user.UserId, SubmittedAtUtc = now, CreatedAt = now, CreatedBy = user.UserId
            };
            if (!await ValidAccounts(instruction, source.CampusId, ct))
                return Fail<ScholarshipDisbursementRow>("Select active BDT scholarship expense and actual cash/bank ledgers.", 409);
            db.ScholarshipDisbursements.Add(instruction);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(Map(instruction, aid.PublicId));
        }
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipDisbursementRow>("Application changed; reload.", 409); }
        catch (DbUpdateException) { return Fail<ScholarshipDisbursementRow>("Concurrent disbursement; reload.", 409); }
    }

    public async Task<ApiResponse<ScholarshipDisbursementRow>> ReviewAsync(Guid applicationReference,
        ReviewScholarshipDisbursementRequest request, CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<ScholarshipDisbursementRow>("Independent Finance reviewer required.", 403);
        if (request == null || request.RequestId == Guid.Empty || applicationReference == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Fail<ScholarshipDisbursementRow>("Documented review and request reference required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await (from payment in db.ScholarshipDisbursements
                join aid in db.ScholarshipApplications
                    on new { payment.TenantId, Id = payment.ScholarshipApplicationId }
                    equals new { aid.TenantId, aid.Id }
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { aid.TenantId, Id = aid.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where payment.TenantId == user.TenantId && aid.PublicId == applicationReference &&
                    !payment.IsDeleted && !aid.IsDeleted && !enrollment.IsDeleted
                select new { payment, aid, enrollment.StudentId, enrollment.CampusId,
                    enrollment.AcademicProgramId }).SingleOrDefaultAsync(ct);
            if (source == null) return Fail<ScholarshipDisbursementRow>("Disbursement not found.", 404);
            var p = source.payment;
            if (p.ReviewRequestId == request.RequestId && p.ReviewedByUserId == user.UserId &&
                p.ReviewReason == request.Reason.Trim() &&
                p.State == (request.ConfirmPaid ? "Posted" : "Rejected"))
            {
                if (request.ConfirmPaid && (p.JournalId == null ||
                    !await db.Journals.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == p.JournalId && !x.IsDeleted &&
                        x.State == JournalState.Posted && x.SourceType == "ScholarshipDisbursement" &&
                        x.SourceId == p.Id, ct) ||
                    await db.JournalLines.AsNoTracking().Where(x =>
                        x.TenantId == user.TenantId && x.JournalId == p.JournalId)
                        .SumAsync(x => x.DebitAmount - x.CreditAmount, ct) != 0m))
                    return Fail<ScholarshipDisbursementRow>("Posted scholarship GL requires reconciliation.", 409);
                return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
                    await Detail(p, applicationReference, ct));
            }
            if (p.State != "Submitted" || !Version(p.RowVersion, request.RowVersion) ||
                p.PreparedByUserId == user.UserId || source.aid.SubmittedByUserId == user.UserId ||
                source.aid.AwardMode != "CashGrant" || source.aid.State != "Approved" ||
                source.aid.CashAmount != p.Amount || source.aid.StudentDiscountId.HasValue ||
                source.aid.DiscountRuleId.HasValue)
                return Fail<ScholarshipDisbursementRow>("Independent review of unchanged cash grant required.", 409);
            if (await db.ScholarshipDisbursements.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ReviewRequestId == request.RequestId, ct))
                return Fail<ScholarshipDisbursementRow>("Review reference already used.", 409);
            var now = DateTime.UtcNow;
            if (request.ConfirmPaid)
            {
                var businessToday = await InstitutionToday(ct);
                if (!businessToday.HasValue || p.PaymentDate > businessToday.Value ||
                    !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == p.AccountingPeriodId &&
                        !x.IsDeleted && !x.IsClosed && x.StartDate <= p.PaymentDate &&
                        x.EndDate >= p.PaymentDate, ct) ||
                    !await ValidAccounts(p, source.CampusId, ct))
                    return Fail<ScholarshipDisbursementRow>("Period/account validation failed; no journal posted.", 409);
                if (p.GuardianId.HasValue && !await db.StudentGuardians.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.StudentId == source.StudentId &&
                    x.GuardianId == p.GuardianId && !x.IsDeleted && x.IsFinancialContact &&
                    db.Guardians.Any(g => g.TenantId == x.TenantId && g.Id == x.GuardianId &&
                        g.IsActive && !g.IsDeleted), ct))
                    return Fail<ScholarshipDisbursementRow>("Beneficiary authorization changed; reject and reconcile.", 409);
                if (await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && (x.IdempotencyKey == p.PublicId ||
                    (x.SourceType == "ScholarshipDisbursement" && x.SourceId == p.Id)), ct))
                    return Fail<ScholarshipDisbursementRow>("Unlinked scholarship GL exists; reconcile before posting.", 409);
                var journalReference = Guid.NewGuid();
                var journal = new Journal
                {
                    TenantId = user.TenantId, PublicId = journalReference,
                    IdempotencyKey = p.PublicId, JournalNumber = "JRN-" + journalReference.ToString("N").ToUpperInvariant(),
                    JournalDate = p.PaymentDate, AccountingPeriodId = p.AccountingPeriodId,
                    CampusId = source.CampusId, ReferenceNumber = p.ExternalPaymentReference,
                    CurrencyCode = "BDT", State = JournalState.Posted,
                    SourceType = "ScholarshipDisbursement", SourceId = p.Id,
                    Description = "Externally confirmed scholarship " + p.PublicId,
                    ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now, CreatedAt = now, CreatedBy = user.UserId
                };
                db.Journals.Add(journal);
                await db.SaveChangesAsync(ct);
                db.JournalLines.AddRange(
                    new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 1,
                        AccountId = p.ExpenseAccountId, CampusId = source.CampusId,
                        StudentId = source.StudentId, AcademicProgramId = source.AcademicProgramId,
                        DebitAmount = p.Amount, Description = "Scholarship expense",
                        CreatedAt = now, CreatedBy = user.UserId },
                    new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 2,
                        AccountId = p.PaymentAccountId, CampusId = source.CampusId,
                        StudentId = source.StudentId, AcademicProgramId = source.AcademicProgramId,
                        CreditAmount = p.Amount, Description = "External cash/bank payment",
                        CreatedAt = now, CreatedBy = user.UserId });
                p.JournalId = journal.Id;
                source.aid.State = "Awarded";
                source.aid.AwardedAtUtc = now;
                source.aid.UpdatedAt = now; source.aid.UpdatedBy = user.UserId;
            }
            p.State = request.ConfirmPaid ? "Posted" : "Rejected";
            p.ReviewRequestId = request.RequestId; p.ReviewedByUserId = user.UserId;
            p.ReviewedAtUtc = now; p.ReviewReason = request.Reason.Trim();
            p.UpdatedAt = now; p.UpdatedBy = user.UserId;
            db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
            {
                TenantId = user.TenantId, ScholarshipApplicationId = source.aid.Id,
                ClientRequestId = request.RequestId,
                Action = request.ConfirmPaid ? "CashPosted" : "CashRejected",
                Reason = request.Reason.Trim(), ActorUserId = user.UserId,
                CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
                await Detail(p, applicationReference, ct));
        }
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipDisbursementRow>("Concurrent payment review; reload.", 409); }
        catch (DbUpdateException) { return Fail<ScholarshipDisbursementRow>("Disbursement posting conflict; reconcile.", 409); }
    }

    /// <summary>Reversal is an audited two-person process, not an edit of posted GL.</summary>
    public async Task<ApiResponse<ScholarshipDisbursementRow>> RequestReversalAsync(Guid applicationReference,
        ReverseScholarshipDisbursementRequest request, CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<ScholarshipDisbursementRow>("Finance administrator required.", 403);
        var businessToday = await InstitutionToday(ct);
        if (applicationReference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            request.AccountingPeriodId <= 0 || request.ReversalDate.Year is < 2000 or > 2100 ||
            !businessToday.HasValue || request.ReversalDate > businessToday.Value ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500 ||
            string.IsNullOrWhiteSpace(request.ExternalReversalReference) ||
            request.ExternalReversalReference.Trim().Length > 100)
            return Fail<ScholarshipDisbursementRow>("Valid reversal period, evidence reference and reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await (from payment in db.ScholarshipDisbursements
                join aid in db.ScholarshipApplications
                    on new { payment.TenantId, Id = payment.ScholarshipApplicationId }
                    equals new { aid.TenantId, aid.Id }
                where payment.TenantId == user.TenantId && aid.PublicId == applicationReference &&
                    !payment.IsDeleted && !aid.IsDeleted
                select new { payment, aid.PublicId }).SingleOrDefaultAsync(ct);
            if (source == null) return Fail<ScholarshipDisbursementRow>("Disbursement not found.", 404);
            var p = source.payment;
            if (p.State == "ReversalRequested" && p.ReversalRequestId == request.RequestId &&
                p.ReversalRequestedByUserId == user.UserId && p.ReversalDate == request.ReversalDate &&
                p.ReversalAccountingPeriodId == request.AccountingPeriodId &&
                p.ExternalReversalReference == request.ExternalReversalReference.Trim() &&
                p.ReversalReason == request.Reason.Trim())
                return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(await Detail(p, source.PublicId, ct));
            if (p.State != "Posted" || !p.JournalId.HasValue ||
                !Version(p.RowVersion, request.RowVersion) || p.ReversedByUserId.HasValue ||
                request.ReversalDate < p.PaymentDate)
                return Fail<ScholarshipDisbursementRow>("Only an unreversed posted payment can be reversed.", 409);
            if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.Id == request.AccountingPeriodId &&
                !x.IsDeleted && !x.IsClosed && x.StartDate <= request.ReversalDate &&
                x.EndDate >= request.ReversalDate, ct) ||
                await db.ScholarshipApplicationEvents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, ct))
                return Fail<ScholarshipDisbursementRow>("Reversal period closed or request key reused.", 409);
            p.State = "ReversalRequested"; p.ReversalRequestId = request.RequestId;
            p.ReversalRequestedByUserId = user.UserId;
            p.ReversalRequestedAtUtc = DateTime.UtcNow;
            p.ReversalDate = request.ReversalDate;
            p.ReversalAccountingPeriodId = request.AccountingPeriodId;
            p.ExternalReversalReference = request.ExternalReversalReference.Trim();
            p.ReversalReason = request.Reason.Trim();
            p.UpdatedAt = DateTime.UtcNow; p.UpdatedBy = user.UserId;
            db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
            {
                TenantId = user.TenantId, ScholarshipApplicationId = p.ScholarshipApplicationId,
                ClientRequestId = request.RequestId, Action = "CashReversalRequested",
                Reason = p.ReversalReason, ActorUserId = user.UserId, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(await Detail(p, source.PublicId, ct));
        }
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipDisbursementRow>("Concurrent reversal; reload.", 409); }
        catch (DbUpdateException) { return Fail<ScholarshipDisbursementRow>("Reversal conflict; reload.", 409); }
    }

    public async Task<ApiResponse<ScholarshipDisbursementRow>> ReviewReversalAsync(Guid applicationReference,
        ReviewScholarshipReversalRequest request, CancellationToken ct = default)
    {
        if (!await FinanceAdmin(ct)) return Fail<ScholarshipDisbursementRow>("Independent Finance reviewer required.", 403);
        if (applicationReference == Guid.Empty || request == null || request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
            return Fail<ScholarshipDisbursementRow>("Review reference and reason required.");
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var source = await (from p in db.ScholarshipDisbursements
                join aid in db.ScholarshipApplications
                    on new { p.TenantId, Id = p.ScholarshipApplicationId }
                    equals new { aid.TenantId, aid.Id }
                join enrollment in db.StudentEnrollments.AsNoTracking()
                    on new { aid.TenantId, Id = aid.StudentEnrollmentId }
                    equals new { enrollment.TenantId, enrollment.Id }
                where p.TenantId == user.TenantId && aid.PublicId == applicationReference &&
                    !p.IsDeleted && !aid.IsDeleted && !enrollment.IsDeleted
                select new { p, aid, enrollment.StudentId, enrollment.CampusId,
                    enrollment.AcademicProgramId }).SingleOrDefaultAsync(ct);
            if (source == null) return Fail<ScholarshipDisbursementRow>("Disbursement not found.", 404);
            var item = source.p;
            if (item.ReversalReviewRequestId == request.RequestId &&
                item.State == (request.ConfirmReversed ? "Reversed" : "Posted"))
            {
                var priorReview = await db.ScholarshipApplicationEvents.AsNoTracking().AnyAsync(x =>
                    x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId &&
                    x.ScholarshipApplicationId == source.aid.Id && x.ActorUserId == user.UserId &&
                    x.Reason == request.Reason.Trim() &&
                    x.Action == (request.ConfirmReversed ? "CashReversed" : "CashReversalRejected"), ct);
                if (!priorReview || (request.ConfirmReversed &&
                    (!item.ReversalJournalId.HasValue || !await db.Journals.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == item.ReversalJournalId &&
                        x.State == JournalState.Posted && x.SourceType == "ScholarshipDisbursementReversal" &&
                        x.SourceId == item.Id && x.ReversalOfJournalId == item.JournalId, ct))))
                    return Fail<ScholarshipDisbursementRow>("Reversal GL requires reconciliation.", 409);
                return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
                    await Detail(item, applicationReference, ct));
            }
            if (item.State != "ReversalRequested" || !Version(item.RowVersion, request.RowVersion) ||
                !item.JournalId.HasValue || item.ReversalJournalId.HasValue ||
                !item.ReversalDate.HasValue || !item.ReversalAccountingPeriodId.HasValue ||
                item.ReversalRequestedByUserId == user.UserId || source.aid.State != "Awarded")
                return Fail<ScholarshipDisbursementRow>("Independent reversal review of a posted grant required.", 409);
            if (await db.ScholarshipApplicationEvents.AsNoTracking().AnyAsync(x =>
                x.TenantId == user.TenantId && x.ClientRequestId == request.RequestId, ct))
                return Fail<ScholarshipDisbursementRow>("Review reference already used.", 409);
            var now = DateTime.UtcNow;
            if (request.ConfirmReversed)
            {
                var original = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == user.TenantId && x.Id == item.JournalId && !x.IsDeleted &&
                    x.State == JournalState.Posted && x.SourceType == "ScholarshipDisbursement" &&
                    x.SourceId == item.Id && x.CampusId == source.CampusId &&
                    x.CurrencyCode == "BDT", ct);
                var lines = await db.JournalLines.AsNoTracking().Where(x =>
                    x.TenantId == user.TenantId && x.JournalId == item.JournalId && !x.IsDeleted)
                    .OrderBy(x => x.LineNo).ToArrayAsync(ct);
                if (original == null || lines.Length != 2 ||
                    lines[0].AccountId != item.ExpenseAccountId || lines[0].DebitAmount != item.Amount ||
                    lines[0].CreditAmount != 0m || lines[1].AccountId != item.PaymentAccountId ||
                    lines[1].CreditAmount != item.Amount || lines[1].DebitAmount != 0m ||
                    await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.ReversalOfJournalId == item.JournalId &&
                        x.State == JournalState.Posted, ct))
                    return Fail<ScholarshipDisbursementRow>("Original GL already reversed or inconsistent.", 409);
                var businessToday = await InstitutionToday(ct);
                if (!businessToday.HasValue || item.ReversalDate > businessToday.Value ||
                    !await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                        x.TenantId == user.TenantId && x.Id == item.ReversalAccountingPeriodId &&
                        !x.IsDeleted && !x.IsClosed && x.StartDate <= item.ReversalDate &&
                        x.EndDate >= item.ReversalDate, ct))
                    return Fail<ScholarshipDisbursementRow>("Open reversal accounting period required.", 409);
                var journalReference = Guid.NewGuid();
                var journal = new Journal
                {
                    TenantId = user.TenantId, PublicId = journalReference,
                    IdempotencyKey = item.ReversalRequestId,
                    JournalNumber = "JRN-" + journalReference.ToString("N").ToUpperInvariant(),
                    JournalDate = item.ReversalDate.Value, AccountingPeriodId = item.ReversalAccountingPeriodId.Value,
                    CampusId = source.CampusId, ReferenceNumber = item.ExternalReversalReference,
                    CurrencyCode = "BDT", State = JournalState.Posted,
                    SourceType = "ScholarshipDisbursementReversal", SourceId = item.Id,
                    ReversalOfJournalId = original.Id,
                    Description = "Verified external scholarship repayment " + item.PublicId,
                    ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now, CreatedAt = now, CreatedBy = user.UserId
                };
                db.Journals.Add(journal);
                await db.SaveChangesAsync(ct);
                db.JournalLines.AddRange(
                    new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 1,
                        AccountId = item.PaymentAccountId, CampusId = source.CampusId,
                        StudentId = source.StudentId, AcademicProgramId = source.AcademicProgramId,
                        DebitAmount = item.Amount, Description = "Repaid cash/bank scholarship",
                        CreatedAt = now, CreatedBy = user.UserId },
                    new JournalLine { TenantId = user.TenantId, JournalId = journal.Id, LineNo = 2,
                        AccountId = item.ExpenseAccountId, CampusId = source.CampusId,
                        StudentId = source.StudentId, AcademicProgramId = source.AcademicProgramId,
                        CreditAmount = item.Amount, Description = "Scholarship expense reversal",
                        CreatedAt = now, CreatedBy = user.UserId });
                item.ReversalJournalId = journal.Id;
                item.ReversedByUserId = user.UserId; item.ReversedAtUtc = now;
                source.aid.State = "Cancelled"; source.aid.UpdatedAt = now;
                source.aid.UpdatedBy = user.UserId;
            }
            item.State = request.ConfirmReversed ? "Reversed" : "Posted";
            item.ReversalReviewRequestId = request.RequestId;
            item.UpdatedAt = now; item.UpdatedBy = user.UserId;
            db.ScholarshipApplicationEvents.Add(new ScholarshipApplicationEvent
            {
                TenantId = user.TenantId, ScholarshipApplicationId = source.aid.Id,
                ClientRequestId = request.RequestId,
                Action = request.ConfirmReversed ? "CashReversed" : "CashReversalRejected",
                Reason = request.Reason.Trim(), ActorUserId = user.UserId,
                CreatedAt = now, CreatedBy = user.UserId
            });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return ApiResponse<ScholarshipDisbursementRow>.SuccessResponse(
                await Detail(item, applicationReference, ct));
        }
        catch (DbUpdateConcurrencyException) { return Fail<ScholarshipDisbursementRow>("Concurrent reversal review; reload.", 409); }
        catch (DbUpdateException) { return Fail<ScholarshipDisbursementRow>("Reversal posting conflict; reconcile.", 409); }
    }
}
