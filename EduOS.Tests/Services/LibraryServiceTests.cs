using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Library;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;
using Xunit;

namespace EduOS.Tests.Services;

public class LibraryServiceTests
{
    [Fact]
    public async Task Return_with_matching_row_version_closes_issue_and_restores_stock()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var issue = await SeedIssueAsync(context, 101, "Issued", [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(issue.PublicId, new ReturnBookDto
        {
            Action = "Returned",
            FineAmount = 25,
            RowVersion = Convert.ToBase64String(issue.RowVersion)
        });

        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be("Returned");
        response.Data.FineAmount.Should().Be(25);
        var savedIssue = await context.BookIssues.IgnoreQueryFilters().SingleAsync(x => x.Id == issue.Id);
        var savedBook = await context.Books.IgnoreQueryFilters().SingleAsync(x => x.Id == issue.BookId);
        savedIssue.Status.Should().Be("Returned");
        savedIssue.ActualReturnDate.Should().NotBeNull();
        savedBook.AvailableCopies.Should().Be(2);
    }

    [Fact]
    public async Task Return_with_stale_row_version_is_rejected_without_mutating_stock()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var issue = await SeedIssueAsync(context, 101, "Issued", [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(issue.PublicId, new ReturnBookDto
        {
            Action = "Returned",
            RowVersion = Convert.ToBase64String([8, 7, 6, 5, 4, 3, 2, 1])
        });

        response.Success.Should().BeFalse();
        response.StatusCode.Should().Be(409);
        var savedIssue = await context.BookIssues.IgnoreQueryFilters().SingleAsync(x => x.Id == issue.Id);
        var savedBook = await context.Books.IgnoreQueryFilters().SingleAsync(x => x.Id == issue.BookId);
        savedIssue.Status.Should().Be("Issued");
        savedIssue.ActualReturnDate.Should().BeNull();
        savedBook.AvailableCopies.Should().Be(1);
    }

    [Fact]
    public async Task Closing_an_already_closed_issue_remains_idempotent()
    {
        await using var context = CreateContext(CreateOptions(), 101);
        var issue = await SeedIssueAsync(context, 101, "Returned", [1, 2, 3, 4, 5, 6, 7, 8]);
        var service = CreateService(context, 101);

        var response = await service.CloseAsync(issue.PublicId, new ReturnBookDto
        {
            Action = "Returned",
            RowVersion = Convert.ToBase64String([9, 9, 9, 9, 9, 9, 9, 9])
        });

        response.Success.Should().BeTrue();
        response.Data!.Status.Should().Be("Returned");
        var savedBook = await context.Books.IgnoreQueryFilters().SingleAsync(x => x.Id == issue.BookId);
        savedBook.AvailableCopies.Should().Be(1);
    }

    private static LibraryService CreateService(EduOSDbContext context, long tenantId) => new(
        new GenericRepository<Book>(context),
        new GenericRepository<BookIssue>(context),
        new GenericRepository<Student>(context),
        new GenericRepository<Employee>(context),
        context,
        new TestCurrentUser(tenantId),
        TimeProvider.System,
        NullLogger<LibraryService>.Instance);

    private static async Task<BookIssue> SeedIssueAsync(EduOSDbContext context, long tenantId, string status, byte[] rowVersion)
    {
        var book = new Book
        {
            TenantId = tenantId,
            PublicId = Guid.NewGuid(),
            Title = "Concurrency in Practice",
            TotalCopies = 2,
            AvailableCopies = 1,
            IsActive = true,
            RowVersion = [11, 12, 13, 14, 15, 16, 17, 18]
        };
        context.Books.Add(book);
        await context.SaveChangesAsync();

        var issue = new BookIssue
        {
            TenantId = tenantId,
            PublicId = Guid.NewGuid(),
            ClientRequestId = Guid.NewGuid(),
            BookId = book.Id,
            Book = book,
            IssueDate = DateTime.Today.AddDays(-3),
            ReturnDate = DateTime.Today.AddDays(7),
            ActualReturnDate = status == "Issued" ? null : DateTime.Today.AddDays(-1),
            Status = status,
            RowVersion = rowVersion
        };
        context.BookIssues.Add(issue);
        await context.SaveChangesAsync();
        return issue;
    }

    private static DbContextOptions<EduOSDbContext> CreateOptions() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"library-service-{Guid.NewGuid():N}").Options;

    private static EduOSDbContext CreateContext(DbContextOptions<EduOSDbContext> options, long tenantId)
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"), new Claim("TenantId", tenantId.ToString())
        ], "TestAuthentication")) };
        http.Items["TenantId"] = tenantId;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestCurrentUser(long tenantId) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenantId;
        public string? FullName => "Library User";
        public string? Email => "library@example.test";
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => true;
        public IReadOnlyList<string> Roles => ["TenantAdmin"];
        public bool IsInRole(string role) => role == "TenantAdmin";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "EduOS tests";
    }
}
