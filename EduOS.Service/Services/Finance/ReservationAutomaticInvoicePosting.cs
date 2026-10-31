using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;

namespace EduOS.Service.Services.Finance;

/// <summary>
/// Prepares an additional reservation charge using the existing institutional
/// StudentInvoice and Journal sources of truth. The Finance-review transaction
/// creates a draft invoice and approved-but-unposted journal. Only successful
/// Operations consumption activates both in the same serializable transaction.
/// </summary>
public static class ReservationAutomaticInvoicePosting
{
    public static Guid InvoiceKey(long tenantId, string kind, long clearanceId)
    {
        if (tenantId <= 0 || clearanceId <= 0 || kind is not ("HOSTEL" or "TRANSPORT"))
            throw new ArgumentOutOfRangeException(nameof(clearanceId));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"EduOS:ReservationInvoice:{tenantId}:{kind}:{clearanceId}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public sealed record Result(long JournalId, long InvoiceId, string InvoiceNumber);

    public static async Task<(Result? Posting, string? Error)> PrepareAsync(
        EduOSDbContext db, long tenantId, long userId, long requestingUserId,
        string kind, long clearanceId,
        long campusId, long enrollmentId, long studentId, decimal amount,
        long? feeHeadId, long? accountingPeriodId, DateOnly invoiceDate,
        DateTime nowUtc, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenantId || tenantId <= 0 || userId <= 0 ||
            requestingUserId <= 0 || requestingUserId == userId ||
            campusId <= 0 || enrollmentId <= 0 || studentId <= 0 ||
            !feeHeadId.HasValue || feeHeadId <= 0 ||
            !accountingPeriodId.HasValue || accountingPeriodId <= 0 ||
            !ReservationPeriodSettlement.ValidAmount(amount) || amount <= 0m ||
            invoiceDate == default || clearanceId <= 0 || kind is not ("HOSTEL" or "TRANSPORT"))
            return (null, "Positive auto invoice requires a valid fee head, open period and amount.");

        var enrollment = await db.StudentEnrollments.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == enrollmentId && x.StudentId == studentId &&
            x.CampusId == campusId && !x.IsDeleted && x.IsCurrent &&
            x.State == EnrollmentState.Active, ct);
        var period = await db.AccountingPeriods.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == accountingPeriodId && !x.IsDeleted &&
            !x.IsClosed && x.StartDate <= invoiceDate && x.EndDate >= invoiceDate, ct);
        if (enrollment == null || period == null)
            return (null, "Active student enrollment and open accounting period required.");

        var head = await db.FeeHeads.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == feeHeadId && !x.IsDeleted && x.IsActive &&
            x.IncomeAccountId.HasValue && x.ReceivableAccountId.HasValue &&
            x.IncomeAccountId != x.ReceivableAccountId, ct);
        if (head == null) return (null, "Fee head needs active income and receivable accounts.");
        var accounts = await db.Accounts.AsNoTracking().Where(x =>
            x.TenantId == tenantId && (x.Id == head.IncomeAccountId || x.Id == head.ReceivableAccountId) &&
            !x.IsDeleted && x.IsActive && !x.IsGroup && x.AllowsPosting && x.CurrencyCode == "BDT")
            .Select(x => new { x.Id, x.AccountType }).ToArrayAsync(ct);
        if (!accounts.Any(x => x.Id == head.IncomeAccountId && x.AccountType == AccountType.Income) ||
            !accounts.Any(x => x.Id == head.ReceivableAccountId && x.AccountType == AccountType.Asset))
            return (null, "Fee head GL accounts must be active BDT Income/Asset ledgers.");

        var key = InvoiceKey(tenantId, kind, clearanceId);
        if (await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.ClientRequestId == key, ct) ||
            await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.IdempotencyKey == key, ct))
            return (null, "An invoice/journal already exists for this clearance; reconcile before retry.");

        var reference = Guid.NewGuid();
        var invoice = new StudentInvoice
        {
            TenantId = tenantId, PublicId = reference, ClientRequestId = key,
            StudentEnrollmentId = enrollmentId,
            InvoiceNumber = "RSV-" + reference.ToString("N").ToUpperInvariant(),
            InvoiceDate = invoiceDate, DueDate = invoiceDate,
            Subtotal = amount, DiscountAmount = 0m, FineAmount = 0m, TotalAmount = amount,
            PaidAmount = 0m, DueAmount = amount, CurrencyCode = "BDT", State = InvoiceState.Draft,
            CreatedAt = nowUtc, CreatedBy = userId
        };
        db.StudentInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        db.StudentInvoiceLines.Add(new StudentInvoiceLine
        {
            TenantId = tenantId, StudentInvoiceId = invoice.Id, FeeHeadId = head.Id,
            FeeHeadCodeSnapshot = head.Code, FeeHeadNameSnapshot = head.Name,
            Description = $"{kind} clearance #{clearanceId} additional period charge",
            Amount = amount, DiscountAmount = 0m, FineAmount = 0m, NetAmount = amount,
            CreatedAt = nowUtc, CreatedBy = userId
        });
        var journalReference = Guid.NewGuid();
        var journal = new Journal
        {
            TenantId = tenantId, PublicId = journalReference, IdempotencyKey = key,
            JournalNumber = "JRN-" + journalReference.ToString("N").ToUpperInvariant(),
            JournalDate = invoiceDate, AccountingPeriodId = period.Id, CampusId = campusId,
            ReferenceNumber = invoice.InvoiceNumber, CurrencyCode = "BDT", State = JournalState.Approved,
            SourceType = "StudentInvoice", SourceId = invoice.Id,
            Description = $"{kind} clearance #{clearanceId} invoice {invoice.InvoiceNumber}",
            ApprovedByUserId = userId, ApprovedAt = nowUtc,
            // The reservation requester is the maker; Finance is the independent checker.
            CreatedAt = nowUtc, CreatedBy = requestingUserId
        };
        db.Journals.Add(journal);
        await db.SaveChangesAsync(ct);
        db.JournalLines.AddRange(
            new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 1,
                AccountId = head.ReceivableAccountId!.Value, CampusId = campusId,
                StudentId = studentId, AcademicProgramId = enrollment.AcademicProgramId,
                DebitAmount = amount, Description = "Student reservation receivable",
                CreatedAt = nowUtc, CreatedBy = userId },
            new JournalLine { TenantId = tenantId, JournalId = journal.Id, LineNo = 2,
                AccountId = head.IncomeAccountId!.Value, CampusId = campusId,
                StudentId = studentId, AcademicProgramId = enrollment.AcademicProgramId,
                FeeHeadId = head.Id, CreditAmount = amount, Description = "Student reservation income",
                CreatedAt = nowUtc, CreatedBy = userId });
        invoice.JournalId = journal.Id;
        invoice.UpdatedAt = nowUtc; invoice.UpdatedBy = userId;
        await db.SaveChangesAsync(ct);
        return (new Result(journal.Id, invoice.Id, invoice.InvoiceNumber), null);
    }

    /// <summary>Verifies a Finance-approved, not-yet-posted document for one exact clearance.</summary>
    public static async Task<bool> PreparedAsync(EduOSDbContext db, long tenantId,
        string kind, long clearanceId, long journalId, long campusId,
        long studentId, long enrollmentId, decimal amount, CancellationToken ct)
    {
        if (db.CurrentTenantId != tenantId || journalId <= 0 ||
            !ReservationPeriodSettlement.ValidAmount(amount) || amount <= 0m) return false;
        var key = InvoiceKey(tenantId, kind, clearanceId);
        var invoice = await db.StudentInvoices.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.ClientRequestId == key && !x.IsDeleted &&
            x.StudentEnrollmentId == enrollmentId && x.JournalId == journalId &&
            x.CurrencyCode == "BDT" && x.State == InvoiceState.Draft &&
            x.TotalAmount == amount && x.PaidAmount == 0m && x.DueAmount == amount, ct);
        var journal = await db.Journals.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenantId && x.Id == journalId && !x.IsDeleted &&
            x.IdempotencyKey == key && x.State == JournalState.Approved &&
            x.SourceType == "StudentInvoice" && x.CampusId == campusId &&
            x.CurrencyCode == "BDT" && x.PostedByUserId == null &&
            x.ApprovedByUserId.HasValue && x.CreatedBy != x.ApprovedByUserId, ct);
        if (invoice == null || journal == null || journal.SourceId != invoice.Id ||
            journal.ReferenceNumber != invoice.InvoiceNumber ||
            await db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.State == JournalState.Posted &&
                x.ReversalOfJournalId == journalId, ct)) return false;
        // Finance may approve today but Operations may consume days later. Never
        // post into an accounting period closed between the two transactions.
        if (!await db.AccountingPeriods.AsNoTracking().AnyAsync(x =>
                x.TenantId == tenantId && x.Id == journal.AccountingPeriodId &&
                !x.IsDeleted && !x.IsClosed && x.StartDate <= journal.JournalDate &&
                x.EndDate >= journal.JournalDate, ct) ||
            invoice.InvoiceDate != journal.JournalDate) return false;
        var lines = await db.JournalLines.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.TenantId == tenantId && x.JournalId == journalId).Take(101).ToArrayAsync(ct);
        if (lines.Length != 2 || lines.Any(x => x.IsDeleted || x.TenantId != tenantId ||
            x.JournalId != journalId || x.CampusId != campusId || x.StudentId != studentId) ||
            lines.Sum(x => x.DebitAmount) != amount || lines.Sum(x => x.CreditAmount) != amount ||
            lines.Count(x => x.DebitAmount == amount && x.CreditAmount == 0m) != 1 ||
            lines.Count(x => x.CreditAmount == amount && x.DebitAmount == 0m) != 1)
            return false;
        var invoiceLines = await db.StudentInvoiceLines.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.StudentInvoiceId == invoice.Id)
            .Take(3).ToArrayAsync(ct);
        return invoiceLines.Length == 1 && !invoiceLines[0].IsDeleted &&
            invoiceLines[0].Amount == amount && invoiceLines[0].NetAmount == amount &&
            invoiceLines[0].DiscountAmount == 0m && invoiceLines[0].FineAmount == 0m &&
            lines.Any(x => x.FeeHeadId == invoiceLines[0].FeeHeadId && x.CreditAmount == amount);
    }

    /// <summary>Finalize the already-approved invoice only inside the operating mutation transaction.</summary>
    public static async Task<bool> FinalizeAsync(EduOSDbContext db, long tenantId,
        string kind, long clearanceId, long journalId, long campusId,
        long studentId, long enrollmentId, decimal amount, long userId,
        DateTime nowUtc, CancellationToken ct)
    {
        if (!await PreparedAsync(db, tenantId, kind, clearanceId, journalId,
                campusId, studentId, enrollmentId, amount, ct)) return false;
        var key = InvoiceKey(tenantId, kind, clearanceId);
        var invoice = await db.StudentInvoices.FirstAsync(x => x.TenantId == tenantId &&
            x.ClientRequestId == key && x.JournalId == journalId, ct);
        var journal = await db.Journals.FirstAsync(x => x.TenantId == tenantId && x.Id == journalId, ct);
        invoice.State = InvoiceState.Issued;
        invoice.UpdatedAt = nowUtc; invoice.UpdatedBy = userId;
        journal.State = JournalState.Posted;
        journal.PostedByUserId = userId; journal.PostedAt = nowUtc;
        journal.UpdatedAt = nowUtc; journal.UpdatedBy = userId;
        await db.SaveChangesAsync(ct);
        return true;
    }
}
