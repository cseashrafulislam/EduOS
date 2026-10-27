using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Service.Services.HR;

/// <summary>
/// Fail-closed history checks for immutable institutional payroll GL postings.
/// Includes legacy soft-deleted evidence; never repairs or mutates journal rows.
/// </summary>
internal static class PayrollPostingHistoryIntegrity
{
    internal static Task<bool> HasDeletedLinesAsync(
        EduOSDbContext db, long tenantId, long journalId, CancellationToken ct) =>
        db.JournalLines.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.JournalId == journalId && x.IsDeleted, ct);

    internal static Task<bool> HasPostedReversalAsync(
        EduOSDbContext db, long tenantId, long journalId, CancellationToken ct) =>
        db.Journals.IgnoreQueryFilters().AsNoTracking().AnyAsync(x =>
            x.TenantId == tenantId && x.ReversalOfJournalId == journalId &&
            (x.State == JournalState.Posted || x.State == JournalState.Reversed), ct);
}
