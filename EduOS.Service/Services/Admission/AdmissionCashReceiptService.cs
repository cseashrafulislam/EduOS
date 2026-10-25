using EduOS.Core.Common;
using EduOS.Core.DTOs.Admission;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Admission;

/// <summary>One-time institutional cash application fee, posted atomically to the existing GL.
/// Never handles online gateways, subscriptions, student invoices, or refunds.</summary>
public sealed class AdmissionCashReceiptService : IAdmissionCashReceiptService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;

    public AdmissionCashReceiptService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }

    private bool Authorized => user.IsAuthenticated && user.TenantId > 0 && user.IsTenantAdmin;
    private static ApiResponse<T> Error<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);
    private static bool Money(decimal amount) => amount > 0m && amount <= 9999999999999999.99m &&
        decimal.Round(amount, 2) == amount;
    private static bool CashLedger(Account a) =>
        a.Code.Contains("cash", StringComparison.OrdinalIgnoreCase) ||
        a.Name.Contains("cash", StringComparison.OrdinalIgnoreCase);
    private static bool Eligible(AdmissionApplicantState state) =>
        state is AdmissionApplicantState.Submitted or AdmissionApplicantState.UnderReview
            or AdmissionApplicantState.DocumentPending or AdmissionApplicantState.AssessmentPending
            or AdmissionApplicantState.Qualified or AdmissionApplicantState.Admitted;
    private static bool Version(byte[] current, string expected)
    {
        try { return current.AsSpan().SequenceEqual(Convert.FromBase64String(expected)); }
        catch (FormatException) { return false; }
    }
    private static Guid PostingKey(long tenant, Guid requestId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:AdmissionCash:" + tenant + ":" + requestId.ToString("N"))).AsSpan(0, 16));

    public async Task<ApiResponse<AdmissionCashReceiptWorkspaceDto>> GetWorkspaceAsync(
        string? search, CancellationToken ct = default)
    {
        if (!Authorized) return Error<AdmissionCashReceiptWorkspaceDto>("Institution administrator access required.", 403);
        if (search?.Length > 100 || !string.IsNullOrWhiteSpace(search) && search.Trim().Length < 2)
            return Error<AdmissionCashReceiptWorkspaceDto>("Search requires 2–100 characters.");
        var tenant = user.TenantId;
        var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT" &&
            (x.AccountType == AccountType.Asset || x.AccountType == AccountType.Income))
            .OrderBy(x => x.Code).Take(500).ToListAsync(ct);
        var bankLedgerIds = await db.BankAccounts.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted).Select(x => x.AccountId).ToArrayAsync(ct);
        var periods = await db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && !x.IsClosed).OrderByDescending(x => x.StartDate).Take(100)
            .Select(x => new AdmissionCashOptionDto
            { Id = x.Id, Name = x.Name, StartDate = x.StartDate, EndDate = x.EndDate }).ToListAsync(ct);
        var result = new AdmissionCashReceiptWorkspaceDto
        {
            Today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime),
            OpenPeriods = periods,
            CashAccounts = accounts.Where(x => x.AccountType == AccountType.Asset &&
                CashLedger(x) && !bankLedgerIds.Contains(x.Id))
                .Select(x => new AdmissionCashOptionDto { Id = x.Id, Name = x.Code + " — " + x.Name }).ToList(),
            IncomeAccounts = accounts.Where(x => x.AccountType == AccountType.Income)
                .Select(x => new AdmissionCashOptionDto { Id = x.Id, Name = x.Code + " — " + x.Name }).ToList()
        };
        if (string.IsNullOrWhiteSpace(search))
            return ApiResponse<AdmissionCashReceiptWorkspaceDto>.SuccessResponse(result);
        var term = search.Trim();
        var receiptApplicantIds = await db.AdmissionPayments.AsNoTracking()
            .Where(x => x.TenantId == tenant && !x.IsDeleted && x.ReceiptNumber.StartsWith(term))
            .Select(x => x.AdmissionApplicantId).Take(50).ToArrayAsync(ct);
        var rows = await (from a in db.AdmissionApplicants.AsNoTracking()
            join f in db.AdmissionIntakeForms.AsNoTracking()
                on new { a.TenantId, Id = a.AdmissionIntakeFormId } equals new { f.TenantId, f.Id }
            join c in db.Campuses.AsNoTracking()
                on new { f.TenantId, Id = f.CampusId } equals new { c.TenantId, c.Id }
            where a.TenantId == tenant && !a.IsDeleted && !f.IsDeleted && !c.IsDeleted &&
                (a.ApplicationNumber.Contains(term) || a.FullName.Contains(term) ||
                 receiptApplicantIds.Contains(a.Id))
            orderby a.SubmittedAt descending, a.Id descending
            select new { a.Id, a.PublicId, a.ApplicationNumber, a.FullName, a.State,
                a.RowVersion, f.ApplicationFee, f.CurrencyCode, f.CampusId, CampusName = c.Name })
            .Take(50).ToListAsync(ct);
        var ids = rows.Select(x => x.Id).ToArray();
        var payments = await db.AdmissionPayments.AsNoTracking().Where(x => x.TenantId == tenant &&
            !x.IsDeleted && x.State == PaymentState.Successful && ids.Contains(x.AdmissionApplicantId))
            .Select(x => new { x.AdmissionApplicantId, x.PublicId, x.ReceiptNumber }).ToListAsync(ct);
        var byApplicant = payments.GroupBy(x => x.AdmissionApplicantId)
            .ToDictionary(x => x.Key, x => x.First());
        result.Applicants = rows.Select(x =>
        {
            byApplicant.TryGetValue(x.Id, out var paid);
            return new AdmissionCashApplicantDto
            {
                ApplicantReference = x.PublicId, ApplicationNumber = x.ApplicationNumber,
                ApplicantName = x.FullName, CampusId = x.CampusId, CampusName = x.CampusName,
                ApplicationFee = x.ApplicationFee, CurrencyCode = x.CurrencyCode, State = x.State,
                RowVersion = Convert.ToBase64String(x.RowVersion),
                ReceiptReference = paid?.PublicId, ReceiptNumber = paid?.ReceiptNumber
            };
        }).ToList();
        return ApiResponse<AdmissionCashReceiptWorkspaceDto>.SuccessResponse(result);
    }

    public async Task<ApiResponse<AdmissionCashReceiptDto>> GetReceiptAsync(
        Guid receiptReference, CancellationToken ct = default)
    {
        if (!Authorized) return Error<AdmissionCashReceiptDto>("Institution administrator access required.", 403);
        if (receiptReference == Guid.Empty) return Error<AdmissionCashReceiptDto>("Receipt reference required.");
        var payment = await db.AdmissionPayments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == user.TenantId && x.PublicId == receiptReference && !x.IsDeleted, ct);
        if (payment == null) return Error<AdmissionCashReceiptDto>("Receipt not found.", 404);
        var receipt = await ReadReceiptAsync(payment, ct);
        return receipt == null ? Error<AdmissionCashReceiptDto>("Receipt/journal reconciliation required.", 409) :
            ApiResponse<AdmissionCashReceiptDto>.SuccessResponse(receipt);
    }

    private async Task<AdmissionCashReceiptDto?> ReadReceiptAsync(AdmissionPayment payment, CancellationToken ct)
    {
        var tenant = user.TenantId;
        if (payment.State != PaymentState.Successful || payment.PaymentMethod != PaymentMethodType.Cash ||
            !payment.JournalId.HasValue || !Money(payment.Amount)) return null;
        var applicant = await db.AdmissionApplicants.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.AdmissionApplicantId && !x.IsDeleted, ct);
        if (applicant == null) return null;
        var form = await db.AdmissionIntakeForms.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == applicant.AdmissionIntakeFormId && !x.IsDeleted, ct);
        if (form == null) return null;
        var campus = await db.Campuses.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == form.CampusId && !x.IsDeleted, ct);
        var journal = await db.Journals.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.Id == payment.JournalId && !x.IsDeleted &&
            x.SourceType == "AdmissionPayment" && x.SourceId == payment.Id &&
            x.State == JournalState.Posted && x.CampusId == form.CampusId &&
            x.JournalDate == payment.PaymentDate && x.ReferenceNumber == payment.ReceiptNumber, ct);
        if (campus == null || journal == null) return null;
        var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.JournalId == journal.Id && !x.IsDeleted).ToListAsync(ct);
        if (lines.Count != 2 || lines.Count(x => x.DebitAmount == payment.Amount && x.CreditAmount == 0m) != 1 ||
            lines.Count(x => x.CreditAmount == payment.Amount && x.DebitAmount == 0m) != 1 ||
            lines.Any(x => x.CampusId != form.CampusId)) return null;
        return new AdmissionCashReceiptDto
        {
            ReceiptReference = payment.PublicId, ReceiptNumber = payment.ReceiptNumber,
            ApplicantReference = applicant.PublicId, ApplicationNumber = applicant.ApplicationNumber,
            ApplicantName = applicant.FullName, CampusName = campus.Name,
            PaymentDate = payment.PaymentDate, Amount = payment.Amount, CurrencyCode = payment.CurrencyCode,
            State = payment.State, JournalReference = journal.PublicId, JournalNumber = journal.JournalNumber
        };
    }

    public async Task<ApiResponse<AdmissionCashReceiptDto>> CollectAsync(
        CollectAdmissionCashReceiptRequestDto request, CancellationToken ct = default)
    {
        if (!Authorized) return Error<AdmissionCashReceiptDto>("Institution administrator access required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.ApplicantReference == Guid.Empty ||
            request.PaymentDate == default || request.AccountingPeriodId <= 0 || request.CashAccountId <= 0 ||
            request.IncomeAccountId <= 0 || request.CashAccountId == request.IncomeAccountId ||
            !Money(request.ExpectedAmount) || string.IsNullOrWhiteSpace(request.ApplicantRowVersion) ||
            request.ApplicantRowVersion.Length > 256)
            return Error<AdmissionCashReceiptDto>("Applicant, exact fee, request key, cash/income accounts, period and version required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var applicant = await db.AdmissionApplicants.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.ApplicantReference && !x.IsDeleted, ct);
            if (applicant == null) return Error<AdmissionCashReceiptDto>("Applicant not found.", 404);
            var previous = await db.AdmissionPayments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.ClientRequestId == request.ClientRequestId && !x.IsDeleted, ct);
            if (previous != null)
            {
                if (previous.AdmissionApplicantId != applicant.Id || previous.Amount != request.ExpectedAmount ||
                    previous.PaymentDate != request.PaymentDate)
                    return Error<AdmissionCashReceiptDto>("Request key belongs to another receipt.", 409);
                var prior = await ReadReceiptAsync(previous, ct);
                if (prior == null) return Error<AdmissionCashReceiptDto>("Existing receipt requires reconciliation.", 409);
                var journal = await db.Journals.AsNoTracking().FirstAsync(x => x.TenantId == tenant &&
                    x.Id == previous.JournalId, ct);
                var lines = await db.JournalLines.AsNoTracking().Where(x => x.TenantId == tenant &&
                    x.JournalId == journal.Id && !x.IsDeleted).ToListAsync(ct);
                if (journal.AccountingPeriodId != request.AccountingPeriodId ||
                    !lines.Any(x => x.AccountId == request.CashAccountId && x.DebitAmount == previous.Amount) ||
                    !lines.Any(x => x.AccountId == request.IncomeAccountId && x.CreditAmount == previous.Amount))
                    return Error<AdmissionCashReceiptDto>("Request key belongs to different posting accounts.", 409);
                prior.AlreadyRecorded = true;
                return ApiResponse<AdmissionCashReceiptDto>.SuccessResponse(prior, "Existing posted receipt returned.");
            }
            if (!Eligible(applicant.State)) return Error<AdmissionCashReceiptDto>("Application is not eligible for fee collection.", 409);
            if (!Version(applicant.RowVersion, request.ApplicantRowVersion))
                return Error<AdmissionCashReceiptDto>("Applicant changed; reload and retry.", 409);
            var form = await db.AdmissionIntakeForms.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == applicant.AdmissionIntakeFormId && !x.IsDeleted, ct);
            if (form == null || form.CurrencyCode != "BDT" || !Money(form.ApplicationFee) ||
                form.ApplicationFee != request.ExpectedAmount)
                return Error<AdmissionCashReceiptDto>("Application fee or currency changed; refresh before collection.", 409);
            if (!await db.Campuses.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.Id == form.CampusId && x.IsActive && !x.IsDeleted, ct))
                return Error<AdmissionCashReceiptDto>("Application campus is inactive.", 409);
            if (await db.AdmissionPayments.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                x.AdmissionApplicantId == applicant.Id && !x.IsDeleted, ct))
                return Error<AdmissionCashReceiptDto>("Application already has a payment attempt; reconcile before collecting again.", 409);
            var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == request.AccountingPeriodId && !x.IsDeleted && !x.IsClosed &&
                x.StartDate <= request.PaymentDate && x.EndDate >= request.PaymentDate, ct);
            if (period == null) return Error<AdmissionCashReceiptDto>("Select an open period covering the payment date.", 409);
            var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
                (x.Id == request.CashAccountId || x.Id == request.IncomeAccountId) &&
                !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
                .ToListAsync(ct);
            var cash = accounts.SingleOrDefault(x => x.Id == request.CashAccountId && x.AccountType == AccountType.Asset);
            var income = accounts.SingleOrDefault(x => x.Id == request.IncomeAccountId && x.AccountType == AccountType.Income);
            if (cash == null || income == null || !CashLedger(cash) ||
                await db.BankAccounts.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    !x.IsDeleted && x.AccountId == cash.Id, ct))
                return Error<AdmissionCashReceiptDto>("Select active BDT cash-in-hand and income ledgers.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var receiptId = Guid.NewGuid();
            var payment = new AdmissionPayment
            {
                TenantId = tenant, PublicId = receiptId, ClientRequestId = request.ClientRequestId,
                AdmissionApplicantId = applicant.Id,
                ReceiptNumber = "ADM-" + receiptId.ToString("N").ToUpperInvariant(),
                PaymentDate = request.PaymentDate, PaymentMethod = PaymentMethodType.Cash,
                Amount = form.ApplicationFee, CurrencyCode = "BDT", State = PaymentState.Initiated,
                InitiatedAt = now, ReceivedByUserId = user.UserId, CreatedAt = now, CreatedBy = user.UserId
            };
            db.AdmissionPayments.Add(payment);
            await db.SaveChangesAsync(ct);
            var journalId = Guid.NewGuid();
            var journal = new Journal
            {
                TenantId = tenant, PublicId = journalId, IdempotencyKey = PostingKey(tenant, request.ClientRequestId),
                JournalNumber = "JRN-" + journalId.ToString("N").ToUpperInvariant(),
                JournalDate = payment.PaymentDate, AccountingPeriodId = period.Id,
                CampusId = form.CampusId, ReferenceNumber = payment.ReceiptNumber,
                CurrencyCode = "BDT", State = JournalState.Posted,
                SourceType = "AdmissionPayment", SourceId = payment.Id,
                Description = "Admission application fee " + applicant.ApplicationNumber,
                ApprovedByUserId = user.UserId, ApprovedAt = now,
                PostedByUserId = user.UserId, PostedAt = now,
                CreatedAt = now, CreatedBy = user.UserId
            };
            db.Journals.Add(journal);
            await db.SaveChangesAsync(ct);
            db.JournalLines.AddRange(
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                    AccountId = cash.Id, CampusId = form.CampusId, AcademicProgramId = form.AcademicProgramId,
                    DebitAmount = payment.Amount, Description = "Admission fee cash received",
                    CreatedAt = now, CreatedBy = user.UserId },
                new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 2,
                    AccountId = income.Id, CampusId = form.CampusId, AcademicProgramId = form.AcademicProgramId,
                    CreditAmount = payment.Amount, Description = "Admission application fee income",
                    CreatedAt = now, CreatedBy = user.UserId });
            payment.JournalId = journal.Id;
            payment.State = PaymentState.Successful;
            payment.CompletedAt = now;
            payment.UpdatedAt = now;
            payment.UpdatedBy = user.UserId;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await GetReceiptAsync(payment.PublicId, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<AdmissionCashReceiptDto>("Payment changed; reload.", 409); }
        catch (DbUpdateException) { return Error<AdmissionCashReceiptDto>("Payment or journal conflict; reconcile before retry.", 409); }
    }
}
