using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryCopyConditionWorkflowTests
{
    private sealed class Actor(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Context(long tenant)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("copy-condition-" + Guid.NewGuid()).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private static LibraryService Service(EduOSDbContext db, long tenant = 101, string role = "TenantAdmin") =>
        new(new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
            new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
            new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
            db, new Actor(tenant, role), TimeProvider.System,
            NullLogger<LibraryService>.Instance,
            branches: new GenericRepository<LibraryBranch>(db),
            renewalAudits: new GenericRepository<AuditLog>(db));
    private static async Task<(BookCopy Copy, BookIssue? Loan)> Seed(EduOSDbContext db, bool issued)
    {
        var book = new Book { TenantId = 101, Title = "Condition lifecycle" };
        var copy = new BookCopy { TenantId = 101, AccessionNumber = "A-101",
            State = issued ? BookCopyState.Issued : BookCopyState.Available };
        db.Books.Add(book); await db.SaveChangesAsync();
        copy.BookId = book.Id; db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        copy.RowVersion = [1, 2, 3, 4];
        if (!issued) return (copy, null);
        var student = new Student { TenantId = 101, PersonId = 1, FullName = "Private borrower",
            StudentCode = "S-101" };
        db.Students.Add(student); await db.SaveChangesAsync();
        var loan = new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
            ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7,
            IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20) };
        db.BookIssues.Add(loan); await db.SaveChangesAsync();
        loan.RowVersion = [5, 6, 7, 8];
        return (copy, loan);
    }

    [Fact]
    public async Task Available_damage_replay_recovery_retirement_and_immutable_audit()
    {
        await using var db = Context(101);
        var (copy, _) = await Seed(db, false);
        var service = Service(db);
        var first = new ChangeLibraryCopyConditionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Damaged,
            RowVersion = Convert.ToBase64String(copy.RowVersion), Reason = "Binding damaged"
        };
        Assert.True((await service.ChangeCopyConditionAsync(copy.Id, first)).Success);
        Assert.True((await service.ChangeCopyConditionAsync(copy.Id, first)).Success);
        Assert.Equal(BookCopyState.Damaged, copy.State);
        Assert.Single(db.AuditLogs.Where(x => x.Action == "LibraryCopy.Condition"));
        Assert.Equal(409, (await service.ChangeCopyConditionAsync(copy.Id, new()
        {
            ClientRequestId = first.ClientRequestId, TargetState = BookCopyState.Lost,
            RowVersion = first.RowVersion, Reason = first.Reason
        })).StatusCode);
        Assert.Equal(403, (await Service(db, role: "Student").ChangeCopyConditionAsync(copy.Id, first)).StatusCode);
        var recover = new ChangeLibraryCopyConditionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Available,
            RowVersion = Convert.ToBase64String(copy.RowVersion), Reason = "Inspected and repaired"
        };
        Assert.True((await service.ChangeCopyConditionAsync(copy.Id, recover)).Success);
        var retire = new ChangeLibraryCopyConditionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Retired,
            RowVersion = Convert.ToBase64String(copy.RowVersion), Reason = "End of service life"
        };
        Assert.True((await service.ChangeCopyConditionAsync(copy.Id, retire)).Success);
        Assert.Equal(409, (await service.ChangeCopyConditionAsync(copy.Id, new()
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Available,
            RowVersion = Convert.ToBase64String(copy.RowVersion), Reason = "Cannot restore retired"
        })).StatusCode);
        var history = await service.GetCopyConditionHistoryAsync(copy.Id, 1, 20);
        Assert.True(history.Success);
        Assert.Equal(3, history.Data!.TotalCount);
    }

    [Fact]
    public async Task Issued_loss_closes_loan_without_fake_return_or_finance_charge()
    {
        await using var db = Context(101);
        var (copy, loan) = await Seed(db, true);
        var service = Service(db);
        var request = new ChangeLibraryCopyConditionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Lost,
            RowVersion = Convert.ToBase64String(copy.RowVersion),
            OpenIssueRowVersion = Convert.ToBase64String(loan!.RowVersion),
            Reason = "Missing copy confirmed after search"
        };
        Assert.True((await service.ChangeCopyConditionAsync(copy.Id, request)).Success);
        Assert.Equal(BookCopyState.Lost, copy.State);
        Assert.Equal(BookIssueState.Lost, loan.State);
        Assert.Null(loan.ReturnDate);
        Assert.Equal(0m, loan.FineAmount);
        var page = await service.GetCopyConditionsAsync(null, "lost", null, 1, 20);
        Assert.True(page.Success);
        Assert.Single(page.Data!.Items);
        Assert.DoesNotContain("Private borrower", System.Text.Json.JsonSerializer.Serialize(page.Data.Items));
    }

    [Fact]
    public async Task Guessed_other_tenant_copy_and_stale_version_are_rejected()
    {
        await using var db = Context(101);
        var (copy, _) = await Seed(db, false);
        var request = new ChangeLibraryCopyConditionRequestDto
        {
            ClientRequestId = Guid.NewGuid(), TargetState = BookCopyState.Lost,
            RowVersion = Convert.ToBase64String(copy.RowVersion), Reason = "Inventory check"
        };
        Assert.Equal(404, (await Service(db, 102).ChangeCopyConditionAsync(copy.Id, request)).StatusCode);
        request.RowVersion = Convert.ToBase64String(new byte[] { 9, 9 });
        Assert.Equal(409, (await Service(db).ChangeCopyConditionAsync(copy.Id, request)).StatusCode);
        Assert.Equal(BookCopyState.Available, copy.State);
    }
}
