using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryLiabilityReadTests
{
    private sealed class Actor(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 27;
        public long TenantId => tenant;
        public string? FullName => null;
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => role == name;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
    private static EduOSDbContext Context()
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "27"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "91")], "Test")) };
        http.Items["TenantId"] = 91L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("liability-read-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
    private static LibraryLiabilityService Service(EduOSDbContext db, long tenant = 91,
        string role = "TenantAdmin") => new(db, new Actor(tenant, role),
            TimeProvider.System, NullLogger<LibraryLiabilityService>.Instance);

    [Fact]
    public async Task Finance_only_read_filters_tenant_and_preserves_deleted_historical_sources()
    {
        await using var db = Context();
        var branch = new LibraryBranch { TenantId = 91, CampusId = 8,
            Name = "Main", Code = "MAIN" };
        var book = new Book { TenantId = 91, Title = "Laboratory safety", ReplacementPrice = 225m };
        var student = new Student { TenantId = 91, StudentCode = "A-01",
            FullName = "Original borrower", StatusCode = "Active" };
        db.AddRange(branch, book, student);
        await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 91, BookId = book.Id,
            LibraryBranchId = branch.Id, AccessionNumber = "AC-01", State = BookCopyState.Lost };
        db.BookCopies.Add(copy); await db.SaveChangesAsync();
        var issue = new BookIssue { TenantId = 91, ClientRequestId = Guid.NewGuid(),
            BookCopyId = copy.Id, StudentId = student.Id,
            IssueDate = new DateOnly(2026, 1, 1), DueDate = new DateOnly(2026, 1, 10),
            ReplacementPriceSnapshot = 225m, State = BookIssueState.Lost };
        db.BookIssues.Add(issue); await db.SaveChangesAsync();
        var incidents = await Service(db).IncidentsAsync("AC-01", 8, 1, 20);
        Assert.True(incidents.Success);
        var single = Assert.Single(incidents.Data!.Items);
        Assert.False(single.Assessed);
        Assert.Equal(225m, single.ReplacementPrice);
        Assert.Equal(403, (await Service(db, role: "Student").IncidentsAsync(null, null, 1, 20)).StatusCode);
        Assert.Equal(403, (await Service(db, tenant: 92).IncidentsAsync(null, null, 1, 20)).StatusCode);
        db.LibraryLiabilities.Add(new LibraryLiability
        {
            TenantId = 91, BookIssueId = issue.Id, StudentEnrollmentId = 10,
            CampusId = 8, Kind = LibraryLiabilityKind.Lost,
            Amount = 225m, AssessmentDate = new DateOnly(2026, 10, 30),
            Reason = "Lost", AssessedByUserId = 1, ClientRequestId = Guid.NewGuid()
        });
        await db.SaveChangesAsync();
        var after = await Service(db).IncidentsAsync(null, 8, 1, 20);
        Assert.True(Assert.Single(after.Data!.Items).Assessed);
        var noCampus = await Service(db).IncidentsAsync(null, 99, 1, 20);
        Assert.Empty(noCampus.Data!.Items);
        book.IsDeleted = true;
        await db.SaveChangesAsync();
        var history = await Service(db).RegisterAsync("assessed", 8, 1, 20);
        Assert.True(history.Success);
        Assert.Equal("Laboratory safety", Assert.Single(history.Data!.Items).BookTitle);
        Assert.Empty((await Service(db).RegisterAsync("assessed", 99, 1, 20)).Data!.Items);
    }

    [Fact]
    public async Task Overdue_candidates_do_not_apply_a_policy_created_after_the_original_loan()
    {
        await using var db = Context();
        var today = new DateOnly(2026, 10, 30);
        var campus = new LibraryBranch { TenantId = 91, CampusId = 8, Name = "Central", Code = "CENTRAL" };
        var book = new Book { TenantId = 91, Title = "Algebra" };
        var student = new Student { TenantId = 91, StudentCode = "S1", FullName = "Borrower" };
        db.AddRange(campus, book, student); await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 91, BookId = book.Id, LibraryBranchId = campus.Id,
            AccessionNumber = "BK-100" };
        db.BookCopies.Add(copy); await db.SaveChangesAsync();
        var issue = new BookIssue { TenantId = 91, ClientRequestId = Guid.NewGuid(),
            BookCopyId = copy.Id, StudentId = student.Id,
            IssueDate = today.AddDays(-25), DueDate = today.AddDays(-15), ReturnDate = today.AddDays(-10),
            State = BookIssueState.Returned, CreatedAt = today.AddDays(-25).ToDateTime(TimeOnly.MinValue) };
        db.BookIssues.Add(issue); await db.SaveChangesAsync();
        var before = await Service(db).OverdueCandidatesAsync(null, 8, 1, 20);
        Assert.Equal("No issue-date policy", Assert.Single(before.Data!.Items).PolicyStatus);
        db.LibraryFinePolicies.Add(new LibraryFinePolicy { TenantId = 91, CampusId = 8,
            EffectiveFrom = today.AddDays(-27), CreatedAt = today.AddDays(-26).ToDateTime(TimeOnly.MinValue),
            GraceDays = 2, DailyRate = 4.25m, MaximumCharge = 10m });
        await db.SaveChangesAsync();
        Assert.False(await db.LibraryFinePolicies.AsNoTracking().AnyAsync(x => x.TenantId == 91 &&
            x.CampusId == campus.CampusId && x.EffectiveFrom <= issue.IssueDate &&
            x.CreatedAt <= issue.CreatedAt));
        var after = await Service(db).OverdueCandidatesAsync("BK-100", 8, 1, 20);
        Assert.Null(Assert.Single(after.Data!.Items).CalculatedAmount);
        Assert.Equal("No issue-date policy", Assert.Single(after.Data.Items).PolicyStatus);
        var secondCopy = new BookCopy { TenantId = 91, BookId = book.Id,
            LibraryBranchId = campus.Id, AccessionNumber = "BK-101" };
        db.BookCopies.Add(secondCopy); await db.SaveChangesAsync();
        var laterIssue = new BookIssue { TenantId = 91, ClientRequestId = Guid.NewGuid(),
            BookCopyId = secondCopy.Id, StudentId = student.Id,
            IssueDate = today.AddDays(-25), DueDate = today.AddDays(-15),
            ReturnDate = today.AddDays(-10), State = BookIssueState.Returned };
        db.BookIssues.Add(laterIssue); await db.SaveChangesAsync();
        var billable = await Service(db).OverdueCandidatesAsync("BK-101", 8, 1, 20);
        Assert.Equal(10m, Assert.Single(billable.Data!.Items).CalculatedAmount);
        Assert.Equal(5, Assert.Single(after.Data.Items).OverdueDays);
        Assert.Empty((await Service(db).OverdueCandidatesAsync(null, 9, 1, 20)).Data!.Items);
        Assert.Equal(403, (await Service(db, role: "Student")
            .OverdueCandidatesAsync(null, null, 1, 20)).StatusCode);
        Assert.Equal(403, (await Service(db, tenant: 92)
            .FinePoliciesAsync(null, 1, 20)).StatusCode);
    }
}
