using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Library;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.App;

/// <summary>
/// Real SQL Server acceptance for campus-scoped in-app overdue notices.
/// Uses isolated seeded tenants, two independent DbContexts and no external delivery.
/// </summary>
public sealed partial class SqlServerTransportSeatCapacityIntegrationTests
{
    private static LibraryOverdueNoticeService OverdueNoticeService(
        EduOSDbContext db, Fixture fixture) => new(
            db, new DigitalReader(fixture.TenantId, fixture.UserId, "TenantAdmin"),
            TimeProvider.System, NullLogger<LibraryOverdueNoticeService>.Instance);

    private static async Task<(long CampusId, long LoanId)> SeedOverdueNoticeLoanAsync(Fixture fixture)
    {
        await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        var enrollment = await db.StudentEnrollments.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.PublicId == fixture.Enrollments[0]);
        var student = await db.Students.SingleAsync(x =>
            x.TenantId == fixture.TenantId && x.Id == enrollment.StudentId);
        student.UserId = fixture.UserId;
        student.StatusCode = "Active";
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var branch = new LibraryBranch
        {
            TenantId = fixture.TenantId, CampusId = enrollment.CampusId,
            Name = "Overdue notice SQL library", Code = "ON-" + suffix, IsActive = true
        };
        var book = new Book
        {
            TenantId = fixture.TenantId, Title = "Overdue notice SQL book", IsActive = true
        };
        db.AddRange(branch, book);
        await db.SaveChangesAsync();
        var copy = new BookCopy
        {
            TenantId = fixture.TenantId, BookId = book.Id, LibraryBranchId = branch.Id,
            AccessionNumber = "ON-" + suffix, State = BookCopyState.Issued
        };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var loan = new BookIssue
        {
            TenantId = fixture.TenantId, BookCopyId = copy.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), IssuedByUserId = fixture.UserId,
            IssueDate = today.AddDays(-14), DueDate = today.AddDays(-3),
            State = BookIssueState.Issued
        };
        db.BookIssues.Add(loan);
        await db.SaveChangesAsync();
        return (enrollment.CampusId, loan.Id);
    }

    [Fact]
    public async Task V2_library_sql_parallel_same_request_overdue_notice_has_one_audit_and_one_recipient()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var (campusId, loanId) = await SeedOverdueNoticeLoanAsync(fixture);
        var request = new SendLibraryOverdueNoticesRequestDto
        {
            ClientRequestId = Guid.NewGuid(), CampusId = campusId,
            AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow)
        };
        async Task<EduOS.Core.Common.ApiResponse<LibraryOverdueNoticeBatchDto>> Send()
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await OverdueNoticeService(db, fixture).SendBatchAsync(request);
        }
        var attempts = await Task.WhenAll(Send(), Send());
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409,
            "Unexpected SQL contention result " + x.StatusCode + ": " + x.Message));
        await using (var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            Assert.Equal(1, await verify.Notifications.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.SourceType == "BookIssue" &&
                x.SourceId == loanId && x.TypeCode == "LIBRARY_OVERDUE"));
            Assert.Equal(1, await verify.AuditLogs.CountAsync(x =>
                x.TenantId == fixture.TenantId && x.Action == "LibraryOverdueNotice.Send" &&
                x.RequestId == request.ClientRequestId.ToString("N")));
        }
        var replay = await Send();
        Assert.True(replay.Success, replay.Message);
        Assert.True(replay.Data!.AlreadyProcessed);
        Assert.Equal(1, replay.Data.SentCount);
    }

    [Fact]
    public async Task V2_library_sql_competing_batches_and_in_app_opt_out_preserve_single_daily_delivery()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_AUTH_TEST_CONNECTION");
        if (string.IsNullOrWhiteSpace(connection)) return;
        var fixture = await SeedAsync(connection);
        var (campusId, loanId) = await SeedOverdueNoticeLoanAsync(fixture);
        await using (var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId))
        {
            db.NotificationPreferences.Add(new NotificationPreference
            {
                TenantId = fixture.TenantId, UserId = fixture.UserId,
                Channel = NotificationChannelType.InApp,
                EventCode = "LIBRARY_OVERDUE", IsEnabled = false
            });
            await db.SaveChangesAsync();
            var service = OverdueNoticeService(db, fixture);
            var preview = await service.PreviewAsync(campusId);
            Assert.True(preview.Success, preview.Message);
            Assert.Equal(0, preview.Data!.PendingCount);
            var blocked = await service.SendBatchAsync(new SendLibraryOverdueNoticesRequestDto
            {
                ClientRequestId = Guid.NewGuid(), CampusId = campusId,
                AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow)
            });
            Assert.True(blocked.Success, blocked.Message);
            Assert.Equal(0, blocked.Data!.SentCount);
            var preference = await db.NotificationPreferences.SingleAsync(x =>
                x.TenantId == fixture.TenantId && x.UserId == fixture.UserId &&
                x.EventCode == "LIBRARY_OVERDUE");
            preference.IsEnabled = true;
            await db.SaveChangesAsync();
        }
        async Task<EduOS.Core.Common.ApiResponse<LibraryOverdueNoticeBatchDto>> Send(Guid id)
        {
            await using var db = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
            return await OverdueNoticeService(db, fixture).SendBatchAsync(
                new SendLibraryOverdueNoticesRequestDto
                {
                    ClientRequestId = id, CampusId = campusId,
                    AsOfDate = DateOnly.FromDateTime(DateTime.UtcNow)
                });
        }
        var attempts = await Task.WhenAll(Send(Guid.NewGuid()), Send(Guid.NewGuid()));
        Assert.Contains(attempts, x => x.Success);
        Assert.All(attempts, x => Assert.True(x.Success || x.StatusCode == 409,
            "Unexpected SQL contention result " + x.StatusCode + ": " + x.Message));
        Assert.Equal(1, attempts.Where(x => x.Success).Sum(x => x.Data!.SentCount));
        await using var verify = ScopedContext(fixture.Options, fixture.TenantId, fixture.UserId);
        Assert.Equal(1, await verify.Notifications.CountAsync(x =>
            x.TenantId == fixture.TenantId && x.SourceType == "BookIssue" &&
            x.SourceId == loanId && x.TypeCode == "LIBRARY_OVERDUE"));
    }
}
