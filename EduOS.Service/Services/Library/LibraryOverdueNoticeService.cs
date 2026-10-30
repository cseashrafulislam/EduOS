using EduOS.Core.Common;
using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Globalization;

namespace EduOS.Service.Services.Library;

/// <summary>
/// In-app notices for outstanding physical loans. Batches are campus-scoped,
/// limited to 50 and idempotent per loan/day. This never creates a fee or GL entry.
/// </summary>
public sealed class LibraryOverdueNoticeService(
    EduOSDbContext db, ICurrentUserService user, TimeProvider clock,
    ILogger<LibraryOverdueNoticeService> logger) : ILibraryOverdueNoticeService
{
    private const string NoticeType = "LIBRARY_OVERDUE";
    private const string AuditAction = "LibraryOverdueNotice.Send";
    private const int BatchLimit = 50;
    private long Tenant => user.TenantId;
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private static ApiResponse<T> Fail<T>(string message, int status = 400) =>
        ApiResponse<T>.ErrorResponse(message, status);

    private async Task<bool> CanManageCampusAsync(long campusId, CancellationToken ct)
    {
        if (!user.IsAuthenticated || Tenant <= 0 || user.UserId <= 0 ||
            db.CurrentTenantId != Tenant || campusId <= 0)
            return false;
        if (!await db.Campuses.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.Id == campusId && x.IsActive && !x.IsDeleted, ct))
            return false;
        if (user.IsTenantAdmin) return true;
        if (user.IsInRole("Principal"))
            return await db.UserCampusAccesses.AsNoTracking().AnyAsync(x =>
                x.TenantId == Tenant && x.CampusId == campusId && x.UserId == user.UserId &&
                x.IsActive && !x.IsDeleted, ct);
        if (!user.IsInRole("Librarian")) return false;
        var today = DateOnly.FromDateTime(Now);
        var employees = db.Employees.AsNoTracking()
            .Where(x => x.TenantId == Tenant && x.UserId == user.UserId &&
                x.State == EmployeeState.Active && !x.IsDeleted).Select(x => x.Id);
        return await db.EmployeeCampusAssignments.AsNoTracking().AnyAsync(x =>
            x.TenantId == Tenant && x.CampusId == campusId && x.IsCurrent &&
            !x.IsDeleted && employees.Contains(x.EmployeeId) &&
            x.EffectiveFrom <= today &&
            (!x.EffectiveTo.HasValue || x.EffectiveTo.Value >= today), ct);
    }

    private sealed class Candidate
    {
        public long IssueId { get; set; }
        public Guid IssueReference { get; set; }
        public long UserId { get; set; }
        public string StudentCode { get; set; } = string.Empty;
        public string BookTitle { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public DateOnly DueDate { get; set; }
    }

    private IQueryable<Candidate> Pending(long campusId, DateOnly today, DateTime start, DateTime end)
    {
        var tenant = Tenant;
        return from issue in db.BookIssues.AsNoTracking()
               join copy in db.BookCopies.AsNoTracking() on issue.BookCopyId equals copy.Id
               join branch in db.LibraryBranches.AsNoTracking() on copy.LibraryBranchId equals (long?)branch.Id
               join book in db.Books.AsNoTracking() on copy.BookId equals book.Id
               join student in db.Students.AsNoTracking() on issue.StudentId equals student.Id
               where issue.TenantId == tenant && copy.TenantId == tenant &&
                   branch.TenantId == tenant && book.TenantId == tenant && student.TenantId == tenant &&
                   !issue.IsDeleted && !copy.IsDeleted && !branch.IsDeleted &&
                   !book.IsDeleted && !student.IsDeleted && branch.IsActive &&
                   branch.CampusId == campusId && issue.State == BookIssueState.Issued &&
                   copy.State == BookCopyState.Issued && issue.DueDate < today &&
                   student.StatusCode == "Active" && student.UserId.HasValue &&
                   db.StudentEnrollments.Any(e => e.TenantId == tenant &&
                       e.StudentId == student.Id && e.CampusId == campusId &&
                       e.IsCurrent && e.State == EnrollmentState.Active && !e.IsDeleted) &&
                   !db.Notifications.Any(n => n.TenantId == tenant && !n.IsDeleted &&
                       n.TypeCode == NoticeType && n.SourceType == "BookIssue" &&
                       n.SourceId == issue.Id && n.CreatedAt >= start && n.CreatedAt < end)
               select new Candidate
               {
                   IssueId = issue.Id, IssueReference = issue.PublicId,
                   UserId = student.UserId!.Value, StudentCode = student.StudentCode,
                   BookTitle = book.Title, Language = student.PreferredLanguage,
                   DueDate = issue.DueDate
               };
    }

    public async Task<ApiResponse<LibraryOverdueNoticePreviewDto>> PreviewAsync(
        long campusId, CancellationToken ct = default)
    {
        if (!await CanManageCampusAsync(campusId, ct))
            return Fail<LibraryOverdueNoticePreviewDto>("Campus overdue notice permission required.", 403);
        var today = DateOnly.FromDateTime(Now);
        var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var end = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var pending = Pending(campusId, today, start, end);
        var count = await pending.CountAsync(ct);
        var rows = await pending.OrderBy(x => x.DueDate).ThenBy(x => x.IssueId)
            .Take(BatchLimit).ToListAsync(ct);
        return ApiResponse<LibraryOverdueNoticePreviewDto>.SuccessResponse(
            new LibraryOverdueNoticePreviewDto
            {
                CampusId = campusId, AsOfDate = today, PendingCount = count,
                NextRecipients = rows.Select(x => new LibraryOverdueNoticeRowDto
                {
                    IssueReference = x.IssueReference, StudentCode = x.StudentCode,
                    BookTitle = x.BookTitle, DueDate = x.DueDate,
                    DaysOverdue = today.DayNumber - x.DueDate.DayNumber
                }).ToList()
            });
    }

    public async Task<ApiResponse<LibraryOverdueNoticeBatchDto>> SendBatchAsync(
        SendLibraryOverdueNoticesRequestDto request, CancellationToken ct = default)
    {
        if (request == null || request.ClientRequestId == Guid.Empty || request.CampusId <= 0 ||
            request.AsOfDate == default)
            return Fail<LibraryOverdueNoticeBatchDto>("Valid campus, date and request ID required.");
        if (!await CanManageCampusAsync(request.CampusId, ct))
            return Fail<LibraryOverdueNoticeBatchDto>("Campus overdue notice permission required.", 403);
        var today = DateOnly.FromDateTime(Now);
        if (request.AsOfDate != today)
            return Fail<LibraryOverdueNoticeBatchDto>("Notice preview expired. Reload before sending.", 409);
        var tenant = Tenant;
        var requestKey = request.ClientRequestId.ToString("N");
        var signature = request.CampusId.ToString(CultureInfo.InvariantCulture) + "|" +
            today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        try
        {
            return await db.ExecuteInTransactionAsync(async token =>
            {
                // Campus assignment may be revoked between preview and the write.
                if (!await CanManageCampusAsync(request.CampusId, token))
                    return Fail<LibraryOverdueNoticeBatchDto>("Campus access changed.", 403);
                var previous = await db.AuditLogs.AsNoTracking().FirstOrDefaultAsync(x =>
                    x.TenantId == tenant && x.Action == AuditAction &&
                    x.RequestId == requestKey, token);
                if (previous != null)
                {
                    if (previous.OldValue != signature || !int.TryParse(previous.NewValue,
                        NumberStyles.None, CultureInfo.InvariantCulture, out var sent))
                        return Fail<LibraryOverdueNoticeBatchDto>("Request ID already used.", 409);
                    return ApiResponse<LibraryOverdueNoticeBatchDto>.SuccessResponse(
                        new LibraryOverdueNoticeBatchDto
                        {
                            ClientRequestId = request.ClientRequestId, CampusId = request.CampusId,
                            AsOfDate = today, SentCount = sent, AlreadyProcessed = true
                        });
                }
                var start = today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var end = today.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
                var candidates = await Pending(request.CampusId, today, start, end)
                    .OrderBy(x => x.DueDate).ThenBy(x => x.IssueId)
                    .Take(BatchLimit).ToListAsync(token);
                var now = Now;
                foreach (var item in candidates)
                {
                    var bangla = item.Language.Equals("bn-BD", StringComparison.OrdinalIgnoreCase);
                    var title = item.BookTitle.Length > 180 ? item.BookTitle[..180] : item.BookTitle;
                    db.Notifications.Add(new Notification
                    {
                        TenantId = tenant, UserId = item.UserId, TypeCode = NoticeType,
                        SourceType = "BookIssue", SourceId = item.IssueId,
                        Title = bangla ? "লাইব্রেরির বই ফেরতের সময় পেরিয়েছে" : "Library loan overdue",
                        Body = bangla
                            ? "বই: " + title + "। ফেরতের শেষ তারিখ: " + item.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                            : "Book: " + title + ". Due: " + item.DueDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ActionUrl = "/Library/Index", CreatedAt = now, CreatedBy = user.UserId
                    });
                }
                db.AuditLogs.Add(new AuditLog
                {
                    TenantId = tenant, UserId = user.UserId, Action = AuditAction,
                    EntityName = "LibraryCampus", EntityId = request.CampusId,
                    RequestId = requestKey, OldValue = signature,
                    NewValue = candidates.Count.ToString(CultureInfo.InvariantCulture),
                    OccurredAt = now, IsSuccess = true
                });
                await db.SaveChangesAsync(token);
                return ApiResponse<LibraryOverdueNoticeBatchDto>.SuccessResponse(
                    new LibraryOverdueNoticeBatchDto
                    {
                        ClientRequestId = request.ClientRequestId, CampusId = request.CampusId,
                        AsOfDate = today, SentCount = candidates.Count
                    });
            }, IsolationLevel.Serializable, ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Fail<LibraryOverdueNoticeBatchDto>("Concurrent notice batch; reload and retry.", 409);
        }
        catch (Exception ex) when (IsSqlConflict(ex))
        {
            logger.LogWarning(ex, "Library overdue notice contention tenant {TenantId}", tenant);
            return Fail<LibraryOverdueNoticeBatchDto>("Concurrent notice batch; reload and retry.", 409);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogError(ex, "Library overdue notice batch failed tenant {TenantId}", tenant);
            return Fail<LibraryOverdueNoticeBatchDto>("Notice batch failed.", 500);
        }
    }

    private static bool IsSqlConflict(Exception error)
    {
        for (Exception? current = error; current != null; current = current.InnerException)
            if (current is SqlException sql && sql.Number is 1205 or 2601 or 2627 or 3960)
                return true;
        return false;
    }
}
