using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
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

public sealed class LibraryPrincipalCampusAccessTests
{
    [Fact]
    public async Task Employee_direct_loan_is_campus_scoped_idempotent_and_visible_only_to_owner()
    {
        await using var db = Context();
        var seed = await SeedAsync(db);
        var employee = new Employee { TenantId = 101, UserId = 7, PersonId = 301,
            DesignationId = 1, FullName = "Campus faculty", EmployeeCode = "FAC-01",
            State = EmployeeState.Active };
        var outsider = new Employee { TenantId = 101, UserId = 99, PersonId = 302,
            DesignationId = 1, FullName = "Another faculty", EmployeeCode = "FAC-02",
            State = EmployeeState.Active };
        db.AddRange(employee, outsider);
        await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        db.AddRange(
            new EmployeeCampusAssignment { TenantId = 101, EmployeeId = employee.Id,
                CampusId = seed.CampusA.Id, EffectiveFrom = today.AddYears(-1), IsCurrent = true },
            new EmployeeCampusAssignment { TenantId = 101, EmployeeId = outsider.Id,
                CampusId = seed.CampusB.Id, EffectiveFrom = today.AddYears(-1), IsCurrent = true });
        var copy = new BookCopy { TenantId = 101, BookId = seed.Book.Id,
            LibraryBranchId = seed.BranchA.Id, AccessionNumber = "FAC-A-AVAILABLE",
            State = BookCopyState.Available };
        db.BookCopies.Add(copy);
        await db.SaveChangesAsync();
        var librarian = Service(db, "TenantAdmin");
        (await librarian.FindEligibleEmployeesAsync("FAC-01", 1, 20)).Data!.Items
            .Should().ContainSingle().Which.EmployeeReference.Should().Be(employee.PublicId);
        (await librarian.GetAvailableCopiesForEmployeeAsync(seed.Book.PublicId,
            employee.PublicId, 1, 20)).Data!.Items.Select(x => x.Id).Should().Contain(copy.Id);
        (await librarian.GetAvailableCopiesForEmployeeAsync(seed.Book.PublicId,
            outsider.PublicId, 1, 20)).Data!.Items.Select(x => x.Id).Should().NotContain(copy.Id);
        var request = new IssueBookRequestDto { ClientRequestId = Guid.NewGuid(),
            BookCopyId = copy.Id, EmployeeReference = employee.PublicId,
            DueDate = today.AddDays(14) };
        var first = await librarian.IssueEmployeeAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.BorrowerType.Should().Be("Employee");
        first.Data.EmployeeReference.Should().Be(employee.PublicId);
        (await librarian.IssueEmployeeAsync(request)).Success.Should().BeTrue();
        (await librarian.IssueEmployeeAsync(new IssueBookRequestDto { ClientRequestId = Guid.NewGuid(),
            BookCopyId = copy.Id, EmployeeReference = outsider.PublicId,
            DueDate = today.AddDays(14) })).StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync(x => x.BookCopyId == copy.Id)).Should().Be(1);
        (await librarian.GetMyEmployeeIssuesAsync(1, 20, "all")).Data!.Items
            .Should().ContainSingle().Which.Reference.Should().Be(first.Data.Reference);
        var register = await librarian.GetCirculationRegisterAsync("FAC-01", "issued", seed.CampusA.Id, 1, 20);
        register.Data!.Items.Should().ContainSingle().Which.Issue.EmployeeReference.Should().Be(employee.PublicId);
        // EF InMemory does not generate SQL Server rowversion values automatically.
        var savedLoan = await db.BookIssues.SingleAsync(x => x.PublicId == first.Data.Reference);
        savedLoan.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var closed = await librarian.CloseAsync(first.Data.Reference, new ReturnBookRequestDto
        { RowVersion = Convert.ToBase64String(savedLoan.RowVersion) });
        closed.Success.Should().BeTrue(closed.Message);
        (await db.BookCopies.SingleAsync(x => x.Id == copy.Id)).State.Should().Be(BookCopyState.Available);
        (await librarian.GetMyEmployeeIssuesAsync(1, 20, "returned")).Data!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Principal_branch_copy_catalog_circulation_and_writes_are_grant_scoped()
    {
        await using var db = Context();
        var x = await SeedAsync(db);
        var principal = Service(db, "Principal");
        var branches = await principal.GetAssignableBranchesAsync(null, 1, 20);
        branches.Data!.TotalCount.Should().Be(1);
        branches.Data.Items.Single().Id.Should().Be(x.BranchA.Id);
        (await principal.GetBookCopiesAsync(x.Book.PublicId, 1, 20))
            .Data!.Items.Should().ContainSingle().Which.AccessionNumber.Should().Be("A-1");
        (await principal.GetCatalogAsync(null, 1, 20))
            .Data!.Items.Single().TotalCopies.Should().Be(1);
        var register = await principal.GetCirculationRegisterAsync(null, "all", null, 1, 20);
        register.Data!.TotalCount.Should().Be(1);
        register.Data.Items.Single().CampusId.Should().Be(x.CampusA.Id);
        (await principal.GetCirculationRegisterAsync(null, "all", x.CampusB.Id, 1, 20))
            .Data!.TotalCount.Should().Be(0);
        (await principal.SaveBookCopyAsync(null, new SaveBookCopyRequestDto
        { BookReference = x.Book.PublicId, LibraryBranchId = x.BranchB.Id, AccessionNumber = "B-3" }))
            .StatusCode.Should().Be(403);
        (await principal.SaveBookCopyAsync(x.CopyB.Id, new SaveBookCopyRequestDto
        { BookReference = x.Book.PublicId, LibraryBranchId = x.BranchA.Id,
            AccessionNumber = "B-1", RowVersion = Convert.ToBase64String(x.CopyB.RowVersion) }))
            .StatusCode.Should().Be(403);
        (await principal.IssueAsync(new IssueBookRequestDto
        { BookCopyId = x.AvailableB.Id, StudentReference = x.StudentB.PublicId,
            ClientRequestId = Guid.NewGuid(), IssueDate = x.Today, DueDate = x.Today.AddDays(14) }))
            .StatusCode.Should().Be(403);
        (await principal.CloseAsync(x.IssueB.PublicId, new ReturnBookRequestDto
        { RowVersion = Convert.ToBase64String(x.IssueB.RowVersion) })).StatusCode.Should().Be(403);
        (await db.BookCopies.SingleAsync(v => v.Id == x.CopyB.Id)).State.Should().Be(BookCopyState.Issued);
        (await Service(db, "TenantAdmin").GetCirculationRegisterAsync(null, "all", null, 1, 20))
            .Data!.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task Revoked_principal_grant_denies_issue_replay_return_and_readback()
    {
        await using var db = Context();
        var x = await SeedAsync(db);
        var principal = Service(db, "Principal");
        var replay = new IssueBookRequestDto
        { BookCopyId = x.CopyA.Id, StudentReference = x.StudentA.PublicId,
            ClientRequestId = x.IssueA.ClientRequestId, IssueDate = x.IssueA.IssueDate,
            DueDate = x.IssueA.DueDate };
        (await principal.IssueAsync(replay)).Success.Should().BeTrue();
        x.Grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.IssueAsync(replay)).StatusCode.Should().Be(403);
        (await principal.CloseAsync(x.IssueA.PublicId, new ReturnBookRequestDto
        { RowVersion = Convert.ToBase64String(x.IssueA.RowVersion) })).StatusCode.Should().Be(403);
        (await principal.GetAssignableBranchesAsync(null, 1, 20)).Data!.TotalCount.Should().Be(0);
        (await principal.GetCirculationRegisterAsync(null, "issued", null, 1, 20))
            .Data!.TotalCount.Should().Be(0);
    }


    [Fact]
    public async Task Principal_available_copy_picker_excludes_ungranted_campus_and_student()
    {
        await using var db = Context();
        var data = await SeedAsync(db);
        db.BookCopies.Add(new BookCopy { TenantId = 101, BookId = data.Book.Id,
            LibraryBranchId = data.BranchA.Id, AccessionNumber = "A-2",
            State = BookCopyState.Available });
        await db.SaveChangesAsync();
        var principal = Service(db, "Principal");
        var allowed = await principal.GetAvailableCopiesForStudentAsync(
            data.Book.PublicId, data.StudentA.PublicId, 1, 20);
        allowed.Success.Should().BeTrue(allowed.Message);
        allowed.Data!.TotalCount.Should().Be(1);
        allowed.Data.Items.Single().AccessionNumber.Should().Be("A-2");
        (await principal.GetAvailableCopiesForStudentAsync(
            data.Book.PublicId, data.StudentB.PublicId, 1, 20)).StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task Overdue_ageing_report_is_campus_scoped_and_uses_disjoint_age_buckets()
    {
        await using var db = Context();
        var x = await SeedAsync(db);
        x.IssueA.DueDate = x.Today.AddDays(-5);
        x.IssueB.DueDate = x.Today.AddDays(-40);
        await db.SaveChangesAsync();

        var principal = Service(db, "Principal");
        var scoped = await principal.GetOverdueBranchesAsync(null, null, 1, 20);
        scoped.Success.Should().BeTrue();
        scoped.Data!.TotalCount.Should().Be(1);
        var row = scoped.Data.Items.Single();
        row.BranchId.Should().Be(x.BranchA.Id);
        row.CampusId.Should().Be(x.CampusA.Id);
        row.TotalOverdue.Should().Be(1);
        row.Days1To7.Should().Be(1);
        row.Days8To30.Should().Be(0);
        row.Days31Plus.Should().Be(0);
        row.OldestDueDate.Should().Be(x.Today.AddDays(-5));

        (await principal.GetOverdueBranchesAsync(null, x.CampusB.Id, 1, 20))
            .Data!.TotalCount.Should().Be(0);
        var admin = await Service(db, "TenantAdmin").GetOverdueBranchesAsync(null, null, 1, 20);
        admin.Data!.TotalCount.Should().Be(2);
        admin.Data.Items.Sum(x => x.Days31Plus).Should().Be(1);
        (await principal.GetOverdueBranchesAsync(null, null, 1, 101)).Success.Should().BeFalse();
        (await Service(db, "Student").GetOverdueBranchesAsync(null, null, 1, 20))
            .StatusCode.Should().Be(403);
        x.Grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetOverdueBranchesAsync(null, null, 1, 20))
            .Data!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Copy_integrity_report_is_tenant_and_campus_scoped_and_detects_state_discrepancies()
    {
        await using var db = Context();
        var x = await SeedAsync(db);
        x.CopyA.State = BookCopyState.Available; // Open loan on an available copy.
        x.AvailableB.State = BookCopyState.Issued; // Issued copy with no open loan.
        // A tenant-101 context must reject foreign-tenant fixture writes; never
        // bypass the production tenant boundary merely to seed a report test.
        var foreignCopy = new BookCopy
        {
            TenantId = 202, BookId = x.Book.Id, LibraryBranchId = x.BranchA.Id,
            AccessionNumber = "OTHER-TENANT", State = BookCopyState.Issued
        };
        db.BookCopies.Add(foreignCopy);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => db.SaveChangesAsync());
        db.Entry(foreignCopy).State = EntityState.Detached;
        await db.SaveChangesAsync();

        var principal = Service(db, "Principal");
        var scoped = await principal.GetCopyIntegrityAsync(null, null, 1, 20);
        scoped.Success.Should().BeTrue();
        scoped.Data!.TotalCount.Should().Be(1);
        scoped.Data.Items.Single().AccessionNumber.Should().Be("A-1");
        scoped.Data.Items.Single().StateMismatch.Should().BeTrue();
        (await principal.GetCopyIntegrityAsync(null, x.CampusB.Id, 1, 20))
            .Data!.TotalCount.Should().Be(0);

        var admin = await Service(db, "TenantAdmin").GetCopyIntegrityAsync(null, null, 1, 20);
        admin.Data!.TotalCount.Should().Be(2);
        admin.Data.Items.Select(v => v.AccessionNumber).Should().BeEquivalentTo("A-1", "B-2");
        admin.Data.Items.Single(v => v.AccessionNumber == "B-2").OpenLoanCount.Should().Be(0);
        (await Service(db, "Student").GetCopyIntegrityAsync(null, null, 1, 20))
            .StatusCode.Should().Be(403);
        (await principal.GetCopyIntegrityAsync(null, null, 1, 101)).Success.Should().BeFalse();

        x.Grant.IsActive = false;
        await db.SaveChangesAsync();
        (await principal.GetCopyIntegrityAsync(null, null, 1, 20))
            .Data!.TotalCount.Should().Be(0);
    }

    private static async Task<(Campus CampusA, Campus CampusB, LibraryBranch BranchA,
        LibraryBranch BranchB, Book Book, Student StudentA, Student StudentB,
        BookCopy CopyA, BookCopy CopyB, BookCopy AvailableB, BookIssue IssueA,
        BookIssue IssueB, UserCampusAccess Grant, DateOnly Today)> SeedAsync(EduOSDbContext db)
    {
        var a = new Campus { TenantId = 101, Code = "A", Name = "Campus A", IsActive = true };
        var b = new Campus { TenantId = 101, Code = "B", Name = "Campus B", IsActive = true };
        var book = new Book { TenantId = 101, Title = "Shared title", IsActive = true };
        var sa = new Student { TenantId = 101, PersonId = 11, StudentCode = "A-01",
            FullName = "North", StatusCode = "Active" };
        var sb = new Student { TenantId = 101, PersonId = 12, StudentCode = "B-01",
            FullName = "South", StatusCode = "Active" };
        db.AddRange(a, b, book, sa, sb); await db.SaveChangesAsync();
        var ba = new LibraryBranch { TenantId = 101, CampusId = a.Id,
            Code = "AL", Name = "A Library", IsActive = true };
        var bb = new LibraryBranch { TenantId = 101, CampusId = b.Id,
            Code = "BL", Name = "B Library", IsActive = true };
        var grant = new UserCampusAccess { TenantId = 101, UserId = 7,
            CampusId = a.Id, IsActive = true };
        db.AddRange(ba, bb, grant,
            new StudentEnrollment { TenantId = 101, StudentId = sa.Id, CampusId = a.Id,
                IsCurrent = true, State = EnrollmentState.Active, RollNo = "1",
                EnrollmentDate = new DateOnly(2026, 1, 1) },
            new StudentEnrollment { TenantId = 101, StudentId = sb.Id, CampusId = b.Id,
                IsCurrent = true, State = EnrollmentState.Active, RollNo = "2",
                EnrollmentDate = new DateOnly(2026, 1, 1) });
        await db.SaveChangesAsync();
        var ca = new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = ba.Id,
            AccessionNumber = "A-1", State = BookCopyState.Issued };
        var cb = new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = bb.Id,
            AccessionNumber = "B-1", State = BookCopyState.Issued };
        var free = new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = bb.Id,
            AccessionNumber = "B-2", State = BookCopyState.Available };
        db.AddRange(ca, cb, free); await db.SaveChangesAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ia = new BookIssue { TenantId = 101, BookCopyId = ca.Id, StudentId = sa.Id,
            ClientRequestId = Guid.NewGuid(), IssueDate = today.AddDays(-2),
            DueDate = today.AddDays(14), State = BookIssueState.Issued };
        var ib = new BookIssue { TenantId = 101, BookCopyId = cb.Id, StudentId = sb.Id,
            ClientRequestId = Guid.NewGuid(), IssueDate = today.AddDays(-2),
            DueDate = today.AddDays(14), State = BookIssueState.Issued };
        db.AddRange(ia, ib); await db.SaveChangesAsync();
        return (a, b, ba, bb, book, sa, sb, ca, cb, free, ia, ib, grant, today);
    }

    private static EduOSDbContext Context()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("library-principal-" + Guid.NewGuid().ToString("N")).Options;
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, "TenantAdmin"),
            new Claim("TenantId", "101")
        ], "Test")) };
        http.Items["TenantId"] = 101L;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }
    private static LibraryService Service(EduOSDbContext db, string role) => new(
        new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
        new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        db, new User(role), TimeProvider.System, NullLogger<LibraryService>.Instance,
        new GenericRepository<LibraryBranch>(db), new GenericRepository<Employee>(db),
        new GenericRepository<EmployeeCampusAssignment>(db),
        new GenericRepository<UserCampusAccess>(db), new GenericRepository<Campus>(db),
        new GenericRepository<BookReservation>(db));
    private sealed class User(string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => 101;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => role == name;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
