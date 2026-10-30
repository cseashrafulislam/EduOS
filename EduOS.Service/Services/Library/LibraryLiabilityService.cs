using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;

namespace EduOS.Service.Services.Library;

/// <summary>Library damage/loss assessments are never a StudentFine or an automatically posted charge.</summary>
public sealed class LibraryLiabilityService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<LibraryLiabilityService> logger) : ILibraryLiabilityService
{
    private bool Allowed => user.IsAuthenticated && user.IsTenantAdmin && user.TenantId > 0 &&
        db.CurrentTenantId == user.TenantId;
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal value) => value > 0m && value <= 999999999999.99m &&
        decimal.Round(value, 2) == value;
    private static bool Version(byte[] value, string? input)
    {
        try { return !string.IsNullOrWhiteSpace(input) && value.AsSpan().SequenceEqual(Convert.FromBase64String(input)); }
        catch (FormatException) { return false; }
    }
    private static bool PageValid(int page, int size) => page is >= 1 && size is >= 1 and <= 100 &&
        ((long)page - 1) * size <= int.MaxValue;

    public async Task<ApiResponse<PagedResult<LibraryPostingOptionDto>>> PostingOptionsAsync(string type,
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<LibraryPostingOptionDto>>("Institution administrator permission required.", 403);
        if (!PageValid(page, pageSize) || search?.Length > 100 || type is not ("period" or "head"))
            return Error<PagedResult<LibraryPostingOptionDto>>("Invalid posting option filters.");
        var tenant = user.TenantId;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var term = search?.Trim();
        var query = type == "period"
            ? db.AccountingPeriods.AsNoTracking().Where(x => x.TenantId == tenant &&
                !x.IsDeleted && !x.IsClosed && x.StartDate <= today && x.EndDate >= today)
                .Where(x => string.IsNullOrEmpty(term) || x.Name.Contains(term))
                .Select(x => new LibraryPostingOptionDto { Id = x.Id, Label = x.Name })
            : db.FeeHeads.AsNoTracking().Where(x => x.TenantId == tenant &&
                !x.IsDeleted && x.IsActive && x.Code.StartsWith("LIB-") &&
                x.IncomeAccountId.HasValue && x.ReceivableAccountId.HasValue)
                .Where(x => string.IsNullOrEmpty(term) || x.Name.Contains(term) || x.Code.Contains(term))
                .Select(x => new LibraryPostingOptionDto { Id = x.Id, Label = x.Code + " — " + x.Name });
        var count = await query.CountAsync(ct);
        var rows = await query.OrderBy(x => x.Label).ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryPostingOptionDto>>.SuccessResponse(new PagedResult<LibraryPostingOptionDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<PagedResult<LibraryIncidentDto>>> IncidentsAsync(string? search,
        long? campusId, int page, int pageSize, CancellationToken ct = default)
    {
        if (!Allowed) return Error<PagedResult<LibraryIncidentDto>>("Institution administrator permission required.", 403);
        if (!PageValid(page, pageSize) || campusId is <= 0 || search?.Length > 100)
            return Error<PagedResult<LibraryIncidentDto>>("Invalid incident filters.");
        var tenant = user.TenantId;
        var query = from issue in db.BookIssues.AsNoTracking()
                    join copy in db.BookCopies.AsNoTracking() on new { issue.TenantId, Id = issue.BookCopyId }
                        equals new { copy.TenantId, copy.Id }
                    join branch in db.LibraryBranches.AsNoTracking() on new { copy.TenantId, Id = copy.LibraryBranchId }
                        equals new { branch.TenantId, Id = (long?)branch.Id }
                    join book in db.Books.AsNoTracking() on new { copy.TenantId, Id = copy.BookId }
                        equals new { book.TenantId, book.Id }
                    join student in db.Students.AsNoTracking() on new { issue.TenantId, Id = issue.StudentId }
                        equals new { student.TenantId, student.Id }
                    where issue.TenantId == tenant && !issue.IsDeleted && !copy.IsDeleted &&
                        !branch.IsDeleted && !book.IsDeleted && !student.IsDeleted &&
                        ((issue.State == BookIssueState.Lost && copy.State == BookCopyState.Lost) ||
                         (issue.State == BookIssueState.Damaged && copy.State == BookCopyState.Damaged))
                    select new { issue, copy, branch, book, student };
        if (campusId.HasValue) query = query.Where(x => x.branch.CampusId == campusId.Value);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.copy.AccessionNumber.Contains(term) ||
                x.book.Title.Contains(term) || x.student.FullName.Contains(term) ||
                x.student.StudentCode.Contains(term));
        }
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.issue.UpdatedAt).ThenByDescending(x => x.issue.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LibraryIncidentDto
            {
                IssueReference = x.issue.PublicId, AccessionNumber = x.copy.AccessionNumber,
                BookTitle = x.book.Title, StudentName = x.student.FullName,
                StudentCode = x.student.StudentCode, CampusId = x.branch.CampusId,
                Kind = x.issue.State == BookIssueState.Lost ? LibraryLiabilityKind.Lost : LibraryLiabilityKind.Damaged,
                ReplacementPrice = x.issue.ReplacementPriceSnapshot,
                Assessed = db.LibraryLiabilities.Any(c => c.TenantId == tenant &&
                    c.BookIssueId == x.issue.Id)
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryIncidentDto>>.SuccessResponse(new PagedResult<LibraryIncidentDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<PagedResult<LibraryLiabilityDto>>> RegisterAsync(string? status,
        long? campusId, int page, int pageSize, CancellationToken ct = default)
        => await RegisterCoreAsync(status, campusId, page, pageSize, null, ct);

    private async Task<ApiResponse<PagedResult<LibraryLiabilityDto>>> RegisterCoreAsync(string? status,
        long? campusId, int page, int pageSize, Guid? reference, CancellationToken ct)
    {
        if (!Allowed) return Error<PagedResult<LibraryLiabilityDto>>("Institution administrator permission required.", 403);
        status = (status ?? "all").Trim().ToLowerInvariant();
        if (!PageValid(page, pageSize) || campusId is <= 0 ||
            status is not ("all" or "assessed" or "waived" or "posted" or "settled" or "unpaid"))
            return Error<PagedResult<LibraryLiabilityDto>>("Invalid liability filters.");
        var tenant = user.TenantId;
        var query = from charge in db.LibraryLiabilities.IgnoreQueryFilters().AsNoTracking()
                    join issue in db.BookIssues.IgnoreQueryFilters().AsNoTracking() on new { charge.TenantId, Id = charge.BookIssueId }
                        equals new { issue.TenantId, issue.Id }
                    join copy in db.BookCopies.IgnoreQueryFilters().AsNoTracking() on new { issue.TenantId, Id = issue.BookCopyId }
                        equals new { copy.TenantId, copy.Id }
                    join book in db.Books.IgnoreQueryFilters().AsNoTracking() on new { copy.TenantId, Id = copy.BookId }
                        equals new { book.TenantId, book.Id }
                    join student in db.Students.IgnoreQueryFilters().AsNoTracking() on new { issue.TenantId, Id = issue.StudentId }
                        equals new { student.TenantId, student.Id }
                    join invoice in db.StudentInvoices.IgnoreQueryFilters().AsNoTracking() on new { charge.TenantId, Id = charge.StudentInvoiceId }
                        equals new { invoice.TenantId, Id = (long?)invoice.Id } into invoices
                    from invoice in invoices.DefaultIfEmpty()
                    where charge.TenantId == tenant
                    select new { charge, issue, copy, book, student, invoice };
        if (campusId.HasValue) query = query.Where(x => x.charge.CampusId == campusId.Value);
        if (reference.HasValue) query = query.Where(x => x.charge.PublicId == reference.Value);
        if (status == "assessed") query = query.Where(x => x.charge.State == LibraryLiabilityState.Assessed);
        if (status == "waived") query = query.Where(x => x.charge.State == LibraryLiabilityState.Waived);
        if (status == "posted") query = query.Where(x => x.charge.State == LibraryLiabilityState.Posted);
        if (status == "settled") query = query.Where(x => x.charge.State == LibraryLiabilityState.Posted &&
            x.invoice != null && !x.invoice.IsDeleted && x.invoice.DueAmount == 0m &&
            x.invoice.PaidAmount == x.charge.Amount && x.invoice.State == InvoiceState.Paid);
        if (status == "unpaid") query = query.Where(x => x.charge.State == LibraryLiabilityState.Posted &&
            x.invoice != null && !x.invoice.IsDeleted && x.invoice.DueAmount > 0m);
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(x => x.charge.AssessmentDate)
            .ThenByDescending(x => x.charge.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(x => new LibraryLiabilityDto
            {
                Reference = x.charge.PublicId, IssueReference = x.issue.PublicId,
                AccessionNumber = x.copy.AccessionNumber, BookTitle = x.book.Title,
                StudentName = x.student.FullName, StudentCode = x.student.StudentCode,
                CampusId = x.charge.CampusId, Kind = x.charge.Kind,
                State = x.charge.State, Amount = x.charge.Amount,
                AssessmentDate = x.charge.AssessmentDate, Reason = x.charge.Reason,
                ReviewReason = x.charge.ReviewReason,
                InvoiceReference = x.invoice == null ? null : x.invoice.PublicId,
                InvoiceNumber = x.invoice == null ? null : x.invoice.InvoiceNumber,
                PaidAmount = x.invoice == null ? null : x.invoice.PaidAmount,
                DueAmount = x.invoice == null ? null : x.invoice.DueAmount,
                IsSettled = x.charge.State == LibraryLiabilityState.Posted &&
                    x.invoice != null && !x.invoice.IsDeleted && x.invoice.State == InvoiceState.Paid &&
                    x.invoice.PaidAmount == x.charge.Amount && x.invoice.DueAmount == 0m,
                RowVersion = Convert.ToBase64String(x.charge.RowVersion)
            }).ToListAsync(ct);
        return ApiResponse<PagedResult<LibraryLiabilityDto>>.SuccessResponse(new PagedResult<LibraryLiabilityDto>
        { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<LibraryLiabilityDto>> AssessAsync(AssessLibraryLiabilityRequestDto request,
        CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryLiabilityDto>("Institution administrator permission required.", 403);
        if (request == null || request.ClientRequestId == Guid.Empty || request.IssueReference == Guid.Empty ||
            !Money(request.Amount) || string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > 500)
            return Error<LibraryLiabilityDto>("Incident, amount, reason and request ID are required.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var issue = await db.BookIssues.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == request.IssueReference && !x.IsDeleted, ct);
            if (issue == null) return Error<LibraryLiabilityDto>("Loan incident not found.", 404);
            var previous = await db.LibraryLiabilities.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.ClientRequestId == request.ClientRequestId, ct);
            if (previous != null)
            {
                if (previous.BookIssueId != issue.Id || previous.Amount != request.Amount ||
                    previous.Reason != request.Reason.Trim())
                    return Error<LibraryLiabilityDto>("Request ID already used with different terms.", 409);
                return await SingleAsync(previous.PublicId, ct);
            }
            var kind = issue.State switch
            {
                BookIssueState.Lost => LibraryLiabilityKind.Lost,
                BookIssueState.Damaged => LibraryLiabilityKind.Damaged,
                _ => (LibraryLiabilityKind)0
            };
            if (kind == 0) return Error<LibraryLiabilityDto>("Only a recorded lost or damaged loan can be assessed.", 409);
            if (await db.LibraryLiabilities.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                x.BookIssueId == issue.Id, ct))
                return Error<LibraryLiabilityDto>("This loan already has a financial assessment.", 409);
            var context = await (from copy in db.BookCopies.AsNoTracking()
                join book in db.Books.AsNoTracking() on new { copy.TenantId, Id = copy.BookId }
                    equals new { book.TenantId, book.Id }
                join branch in db.LibraryBranches.AsNoTracking() on new { copy.TenantId, Id = copy.LibraryBranchId }
                    equals new { branch.TenantId, Id = (long?)branch.Id }
                where copy.TenantId == tenant && copy.Id == issue.BookCopyId &&
                    !copy.IsDeleted && !book.IsDeleted && !branch.IsDeleted
                select new { copy.State, branch.CampusId }).SingleOrDefaultAsync(ct);
            if (context == null ||
                context.State != (kind == LibraryLiabilityKind.Lost ? BookCopyState.Lost : BookCopyState.Damaged) ||
                !issue.ReplacementPriceSnapshot.HasValue || !Money(issue.ReplacementPriceSnapshot.Value) ||
                request.Amount > issue.ReplacementPriceSnapshot.Value ||
                (kind == LibraryLiabilityKind.Lost && request.Amount != issue.ReplacementPriceSnapshot.Value))
                return Error<LibraryLiabilityDto>("Issue-time replacement price is required; reconcile legacy loans instead of guessing a price.", 409);
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var enrollment = await db.StudentEnrollments.AsNoTracking().Where(x => x.TenantId == tenant &&
                x.StudentId == issue.StudentId && x.CampusId == context.CampusId && !x.IsDeleted &&
                x.State == EnrollmentState.Active && x.IsCurrent &&
                x.EnrollmentDate <= today && (!x.EndDate.HasValue || x.EndDate >= today))
                .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id).Take(2).ToListAsync(ct);
            if (enrollment.Count != 1)
                return Error<LibraryLiabilityDto>("One active same-campus enrollment is required before billing.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            var row = new LibraryLiability
            {
                TenantId = tenant, ClientRequestId = request.ClientRequestId, BookIssueId = issue.Id,
                StudentEnrollmentId = enrollment[0].Id, CampusId = context.CampusId,
                Kind = kind, Amount = request.Amount, Reason = request.Reason.Trim(),
                AssessmentDate = today, AssessedByUserId = user.UserId,
                State = LibraryLiabilityState.Assessed, CreatedBy = user.UserId, CreatedAt = now
            };
            db.LibraryLiabilities.Add(row);
            db.AuditLogs.Add(new AuditLog { TenantId = tenant, UserId = user.UserId,
                Action = "AssessLibraryLiability", EntityName = "BookIssue", EntityId = issue.Id,
                NewValue = $"{kind}: {row.Amount:0.00} BDT", RequestId = request.ClientRequestId.ToString("N"),
                OccurredAt = now, IsSuccess = true });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await SingleAsync(row.PublicId, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<LibraryLiabilityDto>("Incident changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Library assessment conflict tenant {TenantId}", tenant);
            return Error<LibraryLiabilityDto>("Duplicate or conflicting assessment; reload.", 409);
        }
    }

    public async Task<ApiResponse<LibraryLiabilityDto>> ReviewAsync(Guid reference,
        ReviewLibraryLiabilityRequestDto request, CancellationToken ct = default)
    {
        if (!Allowed) return Error<LibraryLiabilityDto>("Institution administrator permission required.", 403);
        if (reference == Guid.Empty || request == null || request.RowVersion?.Length is < 1 or > 256 ||
            (request.Waive && (string.IsNullOrWhiteSpace(request.WaiverReason) ||
                request.WaiverReason.Trim().Length > 500 || request.AccountingPeriodId.HasValue || request.FeeHeadId.HasValue)) ||
            (!request.Waive && (request.AccountingPeriodId is not > 0 || request.FeeHeadId is not > 0 ||
                !string.IsNullOrWhiteSpace(request.WaiverReason))))
            return Error<LibraryLiabilityDto>("Review requires concurrency token and either waiver reason or posting configuration.");
        var tenant = user.TenantId;
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var charge = await db.LibraryLiabilities.FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.PublicId == reference && !x.IsDeleted, ct);
            if (charge == null) return Error<LibraryLiabilityDto>("Assessment not found.", 404);
            if (charge.State != LibraryLiabilityState.Assessed)
                return Error<LibraryLiabilityDto>("Assessment already reviewed; Finance handles posted adjustments.", 409);
            if (!Version(charge.RowVersion, request.RowVersion))
                return Error<LibraryLiabilityDto>("Assessment changed; reload.", 409);
            if (charge.AssessedByUserId == user.UserId)
                return Error<LibraryLiabilityDto>("A different institution administrator must review this charge.", 403);
            var issue = await db.BookIssues.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenant &&
                x.Id == charge.BookIssueId && !x.IsDeleted, ct);
            if (issue == null || issue.State != (charge.Kind == LibraryLiabilityKind.Lost ?
                    BookIssueState.Lost : BookIssueState.Damaged))
                return Error<LibraryLiabilityDto>("Original incident changed; reconcile before review.", 409);
            var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
                x.TenantId == tenant && x.Id == charge.StudentEnrollmentId &&
                x.StudentId == issue.StudentId && x.CampusId == charge.CampusId && !x.IsDeleted, ct);
            if (enrollment == null) return Error<LibraryLiabilityDto>("Enrollment provenance invalid.", 409);
            var now = clock.GetUtcNow().UtcDateTime;
            if (request.Waive)
            {
                charge.State = LibraryLiabilityState.Waived;
                charge.ReviewReason = request.WaiverReason!.Trim();
            }
            else
            {
                if (!await db.BookCopies.AsNoTracking().AnyAsync(x => x.TenantId == tenant &&
                    x.Id == issue.BookCopyId && !x.IsDeleted &&
                    x.State == (charge.Kind == LibraryLiabilityKind.Lost ?
                        BookCopyState.Lost : BookCopyState.Damaged), ct))
                    return Error<LibraryLiabilityDto>("Copy has been recovered or changed; waive or reconcile the assessment.", 409);
                var today = DateOnly.FromDateTime(now);
                var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.AccountingPeriodId && !x.IsDeleted &&
                    !x.IsClosed && x.StartDate <= today && x.EndDate >= today, ct);
                if (period == null) return Error<LibraryLiabilityDto>("Open accounting period must include today.", 409);
                var head = await db.FeeHeads.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Id == request.FeeHeadId && !x.IsDeleted && x.IsActive, ct);
                if (head == null || !head.Code.StartsWith("LIB-", StringComparison.OrdinalIgnoreCase) ||
                    !head.IncomeAccountId.HasValue || !head.ReceivableAccountId.HasValue ||
                    head.IncomeAccountId == head.ReceivableAccountId ||
                    await db.FeeStructureLines.AnyAsync(x => x.TenantId == tenant &&
                        x.FeeHeadId == head.Id && !x.IsDeleted, ct) ||
                    await db.StudentFeeInstallmentPlanLines.AnyAsync(x => x.TenantId == tenant &&
                        x.FeeHeadId == head.Id && !x.IsDeleted, ct))
                    return Error<LibraryLiabilityDto>("Use a dedicated LIB- fee head, not an ordinary tuition head.", 409);
                var ids = new[] { head.IncomeAccountId.Value, head.ReceivableAccountId.Value };
                var accounts = await db.Accounts.AsNoTracking().Where(x => x.TenantId == tenant &&
                    ids.Contains(x.Id) && !x.IsDeleted && x.IsActive && !x.IsGroup &&
                    x.AllowsPosting && x.CurrencyCode == "BDT").ToListAsync(ct);
                if (!accounts.Any(x => x.Id == head.IncomeAccountId && x.AccountType == AccountType.Income) ||
                    !accounts.Any(x => x.Id == head.ReceivableAccountId && x.AccountType == AccountType.Asset))
                    return Error<LibraryLiabilityDto>("Library income and receivable accounts must be active BDT ledgers.", 409);
                if (!Money(charge.Amount) ||
                    await db.StudentInvoices.IgnoreQueryFilters().AnyAsync(x => x.TenantId == tenant &&
                        x.ClientRequestId == charge.ClientRequestId, ct))
                    return Error<LibraryLiabilityDto>("Financial provenance collision; reconcile before posting.", 409);
                var invoiceRef = Guid.NewGuid();
                var invoice = new StudentInvoice
                {
                    TenantId = tenant, PublicId = invoiceRef, ClientRequestId = charge.ClientRequestId,
                    StudentEnrollmentId = enrollment.Id,
                    InvoiceNumber = "LIB-" + invoiceRef.ToString("N").ToUpperInvariant(),
                    InvoiceDate = today, DueDate = today, Subtotal = charge.Amount,
                    DiscountAmount = 0m, FineAmount = 0m, TotalAmount = charge.Amount,
                    PaidAmount = 0m, DueAmount = charge.Amount, CurrencyCode = "BDT",
                    State = InvoiceState.Issued, CreatedBy = user.UserId, CreatedAt = now
                };
                db.StudentInvoices.Add(invoice);
                await db.SaveChangesAsync(ct);
                db.StudentInvoiceLines.Add(new StudentInvoiceLine
                {
                    TenantId = tenant, StudentInvoiceId = invoice.Id, FeeHeadId = head.Id,
                    FeeHeadCodeSnapshot = head.Code, FeeHeadNameSnapshot = head.Name,
                    Description = (charge.Kind == LibraryLiabilityKind.Lost ? "Lost" : "Damaged") +
                        " library copy replacement charge", Amount = charge.Amount,
                    DiscountAmount = 0m, FineAmount = 0m, NetAmount = charge.Amount,
                    CreatedBy = user.UserId, CreatedAt = now
                });
                var journalRef = Guid.NewGuid();
                var journal = new Journal
                {
                    TenantId = tenant, PublicId = journalRef, IdempotencyKey = charge.ClientRequestId,
                    JournalNumber = "JRN-" + journalRef.ToString("N").ToUpperInvariant(),
                    JournalDate = today, AccountingPeriodId = period.Id, CampusId = charge.CampusId,
                    ReferenceNumber = invoice.InvoiceNumber, CurrencyCode = "BDT",
                    State = JournalState.Posted, SourceType = "StudentInvoice", SourceId = invoice.Id,
                    Description = "Library loss/damage " + invoice.InvoiceNumber,
                    ApprovedByUserId = user.UserId, ApprovedAt = now,
                    PostedByUserId = user.UserId, PostedAt = now,
                    CreatedBy = user.UserId, CreatedAt = now
                };
                db.Journals.Add(journal);
                await db.SaveChangesAsync(ct);
                db.JournalLines.AddRange(
                    new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 1,
                        AccountId = head.ReceivableAccountId.Value, CampusId = charge.CampusId,
                        StudentId = issue.StudentId, AcademicProgramId = enrollment.AcademicProgramId,
                        DebitAmount = charge.Amount, CreditAmount = 0m,
                        Description = "Library replacement receivable", CreatedAt = now, CreatedBy = user.UserId },
                    new JournalLine { TenantId = tenant, JournalId = journal.Id, LineNo = 2,
                        AccountId = head.IncomeAccountId.Value, CampusId = charge.CampusId,
                        StudentId = issue.StudentId, AcademicProgramId = enrollment.AcademicProgramId,
                        FeeHeadId = head.Id, CreditAmount = charge.Amount, DebitAmount = 0m,
                        Description = "Library replacement income", CreatedAt = now, CreatedBy = user.UserId });
                invoice.JournalId = journal.Id;
                invoice.UpdatedAt = now; invoice.UpdatedBy = user.UserId;
                charge.StudentInvoiceId = invoice.Id;
                charge.State = LibraryLiabilityState.Posted;
            }
            charge.ReviewedAtUtc = now; charge.ReviewedByUserId = user.UserId;
            charge.UpdatedAt = now; charge.UpdatedBy = user.UserId;
            db.AuditLogs.Add(new AuditLog { TenantId = tenant, UserId = user.UserId,
                Action = request.Waive ? "WaiveLibraryLiability" : "PostLibraryLiability",
                EntityName = "LibraryLiability", EntityId = charge.Id,
                OldValue = "Assessed", NewValue = request.Waive ? charge.ReviewReason :
                    "Posted to StudentInvoice " + charge.StudentInvoiceId,
                OccurredAt = now, IsSuccess = true });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return await SingleAsync(reference, ct);
        }
        catch (DbUpdateConcurrencyException) { return Error<LibraryLiabilityDto>("Assessment or financial state changed; reload.", 409); }
        catch (DbUpdateException ex)
        {
            logger.LogWarning(ex, "Library liability review conflict tenant {TenantId}", tenant);
            return Error<LibraryLiabilityDto>("Financial posting conflict; reconcile before retry.", 409);
        }
    }

    private async Task<ApiResponse<LibraryLiabilityDto>> SingleAsync(Guid reference, CancellationToken ct)
    {
        var result = await RegisterCoreAsync("all", null, 1, 1, reference, ct);
        // Readback uses a scoped SQL join rather than trusting request-body fields.
        if (!result.Success) return Error<LibraryLiabilityDto>(result.Message, result.StatusCode);
        var row = result.Data?.Items.FirstOrDefault(x => x.Reference == reference);
        if (row == null)
            return Error<LibraryLiabilityDto>("Assessment saved; reload register to see it.", 409);
        return ApiResponse<LibraryLiabilityDto>.SuccessResponse(row);
    }
}
