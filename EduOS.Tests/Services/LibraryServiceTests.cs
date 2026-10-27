using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.HR;
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

public sealed class LibraryServiceTests
{
    [Fact]
    public async Task Catalog_is_tenant_scoped_and_stably_paged()
    {
        var options = Options();
        await using (var seed = Context(options, 101))
        {
            seed.Books.AddRange(new Book { TenantId = 101, Title = "Alpha" },
                new Book { TenantId = 101, Title = "Bravo" },
                new Book { TenantId = 101, Title = "Charlie" });
            await seed.SaveChangesAsync();
        }
        await using (var other = Context(options, 202))
        {
            other.Books.Add(new Book { TenantId = 202, Title = "Private" });
            await other.SaveChangesAsync();
        }
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var first = await service.GetCatalogAsync(null, 1, 2);
        var second = await service.GetCatalogAsync(null, 2, 2);
        first.Success.Should().BeTrue();
        first.Data!.TotalCount.Should().Be(3);
        first.Data.Items.Select(x => x.Title).Should().Equal("Alpha", "Bravo");
        second.Data!.Items.Should().ContainSingle().Which.Title.Should().Be("Charlie");
        (await service.GetCatalogAsync(null, 1, 500)).Data!.PageSize.Should().Be(100);
        (await service.GetCatalogAsync(null, int.MaxValue, 100)).Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Canonical_book_save_request_persists_and_appears_in_paged_catalog()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var saved = await service.SaveBookAsync(new SaveBookRequestDto
        {
            Title = "  Data Structures  ", ISBN = "9780000000001", ReplacementPrice = 125.50m
        });
        saved.Success.Should().BeTrue();
        saved.Data!.Title.Should().Be("Data Structures");
        saved.Data.Reference.Should().NotBeEmpty();
        var catalog = await service.GetCatalogAsync("Data Structures", 1, 20);
        catalog.Success.Should().BeTrue();
        catalog.Data!.Items.Should().ContainSingle().Which.Reference.Should().Be(saved.Data.Reference);
        (await db.Books.SingleAsync()).ReplacementPrice.Should().Be(125.50m);
    }

    [Fact]
    public async Task Student_cannot_modify_catalog()
    {
        var options = Options();
        await using var db = Context(options, 101, "Student");
        var result = await Service(db, 101, "Student").SaveBookAsync(new SaveBookRequestDto { Title = "Denied" });
        result.StatusCode.Should().Be(403);
        (await db.Books.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Student_can_only_see_own_issue_history()
    {
        var options = Options();
        await using (var db = Context(options, 101))
        {
            var book = new Book { TenantId = 101, Title = "Library" };
            var mine = new Student { TenantId = 101, UserId = 7, PersonId = 1, StudentCode = "S1", FullName = "Mine" };
            var other = new Student { TenantId = 101, UserId = 8, PersonId = 2, StudentCode = "S2", FullName = "Other" };
            db.AddRange(book, mine, other); await db.SaveChangesAsync();
            var a = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "A" };
            var b = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "B" };
            db.AddRange(a, b); await db.SaveChangesAsync();
            db.BookIssues.AddRange(
                new BookIssue { TenantId = 101, BookCopyId = a.Id, StudentId = mine.Id, ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7, IssueDate = new DateOnly(2026, 10, 1), DueDate = new DateOnly(2026, 10, 20) },
                new BookIssue { TenantId = 101, BookCopyId = b.Id, StudentId = other.Id, ClientRequestId = Guid.NewGuid(), IssuedByUserId = 7, IssueDate = new DateOnly(2026, 10, 2), DueDate = new DateOnly(2026, 10, 21) });
            await db.SaveChangesAsync();
        }
        await using var reader = Context(options, 101, "Student");
        var response = await Service(reader, 101, "Student").GetMyIssuesAsync(1, 20);
        response.Success.Should().BeTrue();
        response.Data!.TotalCount.Should().Be(1);
        response.Data.Items.Should().ContainSingle().Which.StudentName.Should().Be("Mine");
    }

    [Fact]
    public async Task Issue_requires_current_active_enrollment()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, false);
        var result = await Service(db, 101).IssueAsync(Request(student, copy, Guid.NewGuid()));
        result.StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(0);
        (await db.BookCopies.SingleAsync()).State.Should().Be(BookCopyState.Available);
    }

    [Fact]
    public async Task Future_issue_date_cannot_consume_a_copy()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var request = Request(student, copy, Guid.NewGuid());
        request.IssueDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);
        request.DueDate = request.IssueDate.AddDays(7);

        var result = await Service(db, 101).IssueAsync(request);
        result.StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(0);
        copy.State.Should().Be(BookCopyState.Available);
    }

    [Fact]
    public async Task Future_return_date_cannot_release_a_copy_early()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).Success.Should().BeTrue();
        var issue = await db.BookIssues.SingleAsync();
        issue.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();

        var result = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto
        {
            RowVersion = Convert.ToBase64String(issue.RowVersion),
            ReturnDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1)
        });
        result.StatusCode.Should().Be(409);
        issue.State.Should().Be(BookIssueState.Issued);
        copy.State.Should().Be(BookCopyState.Issued);
    }

    [Fact]
    public async Task Issue_is_retry_safe_and_rejects_changed_payload_or_double_issue()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        var key = Guid.NewGuid();
        var first = await service.IssueAsync(Request(student, copy, key));
        first.Success.Should().BeTrue();
        first.Data!.Reference.Should().NotBeEmpty();
        var replay = await service.IssueAsync(Request(student, copy, key));
        replay.Success.Should().BeTrue();
        replay.Data!.Reference.Should().Be(first.Data.Reference);
        var changed = Request(student, copy, key); changed.DueDate = new DateOnly(2026, 11, 1);
        (await service.IssueAsync(changed)).StatusCode.Should().Be(409);
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).StatusCode.Should().Be(409);
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Return_uses_public_reference_and_rejects_stale_version_without_stock_change()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        var issueResult = await service.IssueAsync(Request(student, copy, Guid.NewGuid()));
        issueResult.Success.Should().BeTrue();
        var issue = await db.BookIssues.SingleAsync();
        issue.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var stale = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = Convert.ToBase64String([4, 3, 2, 1]) });
        stale.StatusCode.Should().Be(409);
        issue.State.Should().Be(BookIssueState.Issued);
        copy.State.Should().Be(BookCopyState.Issued);
        var closed = await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = Convert.ToBase64String(issue.RowVersion) });
        closed.Success.Should().BeTrue();
        closed.Data!.State.Should().Be(BookIssueState.Returned);
        copy.State.Should().Be(BookCopyState.Available);
        (await service.CloseAsync(issue.PublicId, new ReturnBookRequestDto { RowVersion = "stale" })).Success.Should().BeTrue();
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Foreign_tenant_student_reference_is_not_issuable()
    {
        var options = Options();
        Guid foreignReference;
        await using (var foreign = Context(options, 202))
        {
            var (student, _) = await SeedBorrower(foreign, 202, true);
            foreignReference = student.PublicId;
        }
        await using var local = Context(options, 101);
        var (_, copy) = await SeedBorrower(local, 101, true);
        var response = await Service(local, 101).IssueAsync(new IssueBookRequestDto
        {
            ClientRequestId = Guid.NewGuid(), BookCopyId = copy.Id, StudentReference = foreignReference,
            IssueDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20)
        });
        response.Success.Should().BeFalse();
        (await local.BookIssues.CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Category_list_is_tenant_scoped_and_student_cannot_write()
    {
        var options = Options();
        await using (var db = Context(options, 101))
        {
            db.BookCategories.Add(new BookCategory { TenantId = 101, Name = "Science", Code = "SCI" });
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 202))
        {
            db.BookCategories.Add(new BookCategory { TenantId = 202, Name = "Private", Code = "PRI" });
            await db.SaveChangesAsync();
        }
        await using var reader = Context(options, 101, "Student");
        var service = Service(reader, 101, "Student");
        (await service.GetCategoriesAsync()).Data.Should().ContainSingle().Which.Code.Should().Be("SCI");
        (await service.SaveCategoryAsync(null, new SaveBookCategoryRequestDto { Name = "Blocked", Code = "BLK" }))
            .StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Category_create_is_retry_safe_and_duplicate_code_is_rejected()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var service = Service(db, 101);
        var request = new SaveBookCategoryRequestDto { Name = "Science", Code = " sci " };
        var created = await service.SaveCategoryAsync(null, request);
        created.Success.Should().BeTrue();
        (await service.SaveCategoryAsync(null, request)).Data!.Id.Should().Be(created.Data!.Id);
        (await service.SaveCategoryAsync(null, new SaveBookCategoryRequestDto { Name = "Other", Code = "SCI" }))
            .StatusCode.Should().Be(409);
        (await db.BookCategories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Category_update_checks_rowversion_and_active_book_references()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var row = new BookCategory { TenantId = 101, Name = "Science", Code = "SCI", RowVersion = [1, 2, 3] };
        db.BookCategories.Add(row); await db.SaveChangesAsync();
        db.Books.Add(new Book { TenantId = 101, Title = "Chemistry", BookCategoryId = row.Id });
        await db.SaveChangesAsync();
        var service = Service(db, 101);
        (await service.SaveCategoryAsync(row.Id, new SaveBookCategoryRequestDto {
            Name = "Science", Code = "SCI", RowVersion = Convert.ToBase64String([9, 9, 9])
        })).StatusCode.Should().Be(409);
        (await service.SaveCategoryAsync(row.Id, new SaveBookCategoryRequestDto {
            Name = "Science", Code = "SCI", IsActive = false, RowVersion = Convert.ToBase64String(row.RowVersion)
        })).StatusCode.Should().Be(409);
        row.IsActive.Should().BeTrue();
    }


    [Fact]
    public async Task Copy_create_is_idempotent_and_student_cannot_write()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var book = new Book { TenantId = 101, Title = "Physics" };
        db.Books.Add(book); await db.SaveChangesAsync();
        var request = new SaveBookCopyRequestDto { BookReference = book.PublicId, AccessionNumber = " phy-001 " };
        (await Service(db, 101, "Student").SaveBookCopyAsync(null, request)).StatusCode.Should().Be(403);
        var service = Service(db, 101);
        var first = await service.SaveBookCopyAsync(null, request);
        first.Success.Should().BeTrue();
        (await service.SaveBookCopyAsync(null, request)).Data!.Id.Should().Be(first.Data!.Id);
        (await service.GetBookCopiesAsync(book.PublicId, 1, 20)).Data!.Items.Should().ContainSingle();
        (await db.BookCopies.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Copy_edit_rejects_stale_version_and_issued_state()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var book = new Book { TenantId = 101, Title = "Physics" };
        db.Books.Add(book); await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "PHY-001", RowVersion = [1, 2, 3] };
        db.BookCopies.Add(copy); await db.SaveChangesAsync();
        var service = Service(db, 101);
        var request = new SaveBookCopyRequestDto { BookReference = book.PublicId, AccessionNumber = "PHY-002",
            RowVersion = Convert.ToBase64String([9, 9, 9]) };
        (await service.SaveBookCopyAsync(copy.Id, request)).StatusCode.Should().Be(409);
        request.RowVersion = Convert.ToBase64String(copy.RowVersion);
        copy.State = BookCopyState.Issued; await db.SaveChangesAsync();
        (await service.SaveBookCopyAsync(copy.Id, request)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Circulation_register_is_tenant_scoped_searchable_and_overdue_only_when_issued()
    {
        var options = Options();
        await using (var db = Context(options, 101))
        {
            var book = new Book { TenantId = 101, Title = "Physics Handbook" };
            var student = new Student { TenantId = 101, PersonId = 1, StudentCode = "S101", FullName = "Learner One" };
            db.AddRange(book, student); await db.SaveChangesAsync();
            var copy = new BookCopy { TenantId = 101, BookId = book.Id, AccessionNumber = "PHY-1" };
            db.BookCopies.Add(copy); await db.SaveChangesAsync();
            var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
            db.BookIssues.AddRange(
                new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
                    ClientRequestId = Guid.NewGuid(), IssueDate = yesterday.AddDays(-10), DueDate = yesterday,
                    State = BookIssueState.Issued },
                new BookIssue { TenantId = 101, BookCopyId = copy.Id, StudentId = student.Id,
                    ClientRequestId = Guid.NewGuid(), IssueDate = yesterday.AddDays(-10), DueDate = yesterday,
                    State = BookIssueState.Returned, ReturnDate = yesterday });
            await db.SaveChangesAsync();
        }
        await using (var db = Context(options, 202))
        {
            var book = new Book { TenantId = 202, Title = "Secret" };
            var student = new Student { TenantId = 202, PersonId = 2, StudentCode = "S202", FullName = "Other" };
            db.AddRange(book, student); await db.SaveChangesAsync();
            var copy = new BookCopy { TenantId = 202, BookId = book.Id, AccessionNumber = "SECRET" };
            db.BookCopies.Add(copy); await db.SaveChangesAsync();
            db.BookIssues.Add(new BookIssue { TenantId = 202, BookCopyId = copy.Id, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), IssueDate = new DateOnly(2026, 1, 1),
                DueDate = new DateOnly(2026, 1, 2), State = BookIssueState.Issued });
            await db.SaveChangesAsync();
        }
        await using var reader = Context(options, 101);
        var service = Service(reader, 101);
        var overdue = await service.GetCirculationRegisterAsync("PHY", "overdue", null, 1, 20);
        overdue.Success.Should().BeTrue();
        overdue.Data!.TotalCount.Should().Be(1);
        overdue.Data.Items.Single().DaysOverdue.Should().BeGreaterThan(0);
        overdue.Data.Items.Single().Issue.BookTitle.Should().Be("Physics Handbook");
        (await service.GetCirculationRegisterAsync(null, "all", null, 1, 20)).Data!.TotalCount.Should().Be(2);
        (await service.GetCirculationRegisterAsync("Secret", "all", null, 1, 20)).Data!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Circulation_register_rejects_students_and_invalid_filters()
    {
        var options = Options();
        await using var db = Context(options, 101);
        (await Service(db, 101, "Student").GetCirculationRegisterAsync(null, "all", null, 1, 20))
            .StatusCode.Should().Be(403);
        (await Service(db, 101).GetCirculationRegisterAsync(null, "unexpected", null, 1, 20))
            .StatusCode.Should().Be(400);
        (await Service(db, 101).GetCirculationRegisterAsync(null, "issued", 0, 1, 20))
            .StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Circulation_campus_filter_and_cross_campus_issue_are_guarded()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var branch = new LibraryBranch { TenantId = 101, CampusId = 44, Name = "North", Code = "N" };
        db.Add(branch); await db.SaveChangesAsync();
        copy.LibraryBranchId = branch.Id;
        var academic = await db.StudentEnrollments.SingleAsync();
        academic.CampusId = 55;
        await db.SaveChangesAsync();
        var service = Service(db, 101);
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).StatusCode.Should().Be(409);
        academic.CampusId = 44;await db.SaveChangesAsync();
        var issued = await service.IssueAsync(Request(student, copy, Guid.NewGuid()));
        issued.Success.Should().BeTrue(issued.Message);
        (await service.GetCirculationRegisterAsync(null, "issued", 44, 1, 20)).Data!.TotalCount.Should().Be(1);
        (await service.GetCirculationRegisterAsync(null, "issued", 55, 1, 20)).Data!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Librarian_circulation_and_return_are_restricted_to_active_staff_campus()
    {
        var options = Options();
        await using var db = Context(options, 101, "Librarian");
        var (student, copy) = await SeedBorrower(db, 101, true);
        var branch = new LibraryBranch { TenantId = 101, CampusId = 44, Name = "North", Code = "N" };
        var staff = new Employee { TenantId = 101, UserId = 7, PersonId = 3,
            DesignationId = 1, FullName = "Librarian", EmployeeCode = "LIB-1", State = EmployeeState.Active };
        db.AddRange(branch, staff); await db.SaveChangesAsync();
        db.Add(new EmployeeCampusAssignment { TenantId = 101, EmployeeId = staff.Id, CampusId = 44,
            IsCurrent = true, EffectiveFrom = new DateOnly(2025, 1, 1) });
        copy.LibraryBranchId = branch.Id;
        var academic = await db.StudentEnrollments.SingleAsync();
        academic.CampusId = 44;
        await db.SaveChangesAsync();
        var service = Service(db, 101, "Librarian");
        var issued = await service.IssueAsync(Request(student, copy, Guid.NewGuid()));
        issued.Success.Should().BeTrue(issued.Message);
        (await service.GetCirculationRegisterAsync(null, "issued", null, 1, 20))
            .Data!.TotalCount.Should().Be(1);
        var campusAssignment = await db.Set<EmployeeCampusAssignment>().SingleAsync();
        campusAssignment.CampusId = 55;await db.SaveChangesAsync();
        (await service.GetCirculationRegisterAsync(null, "issued", null, 1, 20))
            .Data!.TotalCount.Should().Be(0);
        (await service.CloseAsync(issued.Data!.Reference, new ReturnBookRequestDto
        { RowVersion = issued.Data.RowVersion })).StatusCode.Should().Be(403);
        (await db.BookCopies.SingleAsync()).State.Should().Be(BookCopyState.Issued);
    }


    [Fact]
    public async Task Copy_branch_search_and_administration_are_campus_scoped_for_librarians()
    {
        var options = Options();
        await using var db = Context(options, 101, "Librarian");
        var book = new Book { TenantId = 101, Title = "Shared Catalog" };
        var north = new LibraryBranch { TenantId = 101, CampusId = 44, Name = "North", Code = "N" };
        var south = new LibraryBranch { TenantId = 101, CampusId = 55, Name = "South", Code = "S" };
        var staff = new Employee { TenantId = 101, UserId = 7, PersonId = 3,
            DesignationId = 1, FullName = "Librarian", EmployeeCode = "LIB-2", State = EmployeeState.Active };
        db.AddRange(book, north, south, staff);
        await db.SaveChangesAsync();
        db.Add(new EmployeeCampusAssignment { TenantId = 101, EmployeeId = staff.Id, CampusId = 44,
            IsCurrent = true, EffectiveFrom = new DateOnly(2025, 1, 1) });
        db.BookCopies.Add(new BookCopy { TenantId = 101, BookId = book.Id, LibraryBranchId = south.Id,
            AccessionNumber = "S-001" });
        await db.SaveChangesAsync();
        var service = Service(db, 101, "Librarian");

        var branches = await service.GetAssignableBranchesAsync(null, 1, 20);
        branches.Success.Should().BeTrue();
        branches.Data!.TotalCount.Should().Be(1);
        branches.Data.Items.Single().Id.Should().Be(north.Id);
        (await service.GetAssignableBranchesAsync("South", 1, 20)).Data!.Items.Should().BeEmpty();
        (await service.GetBookCopiesAsync(book.PublicId, 1, 20)).Data!.TotalCount.Should().Be(0);
        (await service.SaveBookCopyAsync(null, new SaveBookCopyRequestDto
        { BookReference = book.PublicId, AccessionNumber = "S-002", LibraryBranchId = south.Id }))
            .StatusCode.Should().Be(403);
        (await service.SaveBookCopyAsync(null, new SaveBookCopyRequestDto
        { BookReference = book.PublicId, AccessionNumber = "UNASSIGNED" }))
            .StatusCode.Should().Be(403);

        var saved = await service.SaveBookCopyAsync(null, new SaveBookCopyRequestDto
        { BookReference = book.PublicId, AccessionNumber = "N-001", LibraryBranchId = north.Id });
        saved.Success.Should().BeTrue(saved.Message);
        (await service.GetBookCopiesAsync(book.PublicId, 1, 20)).Data!.Items
            .Should().ContainSingle().Which.AccessionNumber.Should().Be("N-001");
        var foreignCopy = await db.BookCopies.SingleAsync(x => x.AccessionNumber == "S-001");
        (await service.SaveBookCopyAsync(foreignCopy.Id, new SaveBookCopyRequestDto
        { BookReference = book.PublicId, AccessionNumber = "S-001", LibraryBranchId = north.Id,
            RowVersion = Convert.ToBase64String(foreignCopy.RowVersion) })).StatusCode.Should().Be(403);
        var stock = await service.GetCatalogAsync(null, 1, 20);
        stock.Data!.Items.Single().TotalCopies.Should().Be(1);
        stock.Data.Items.Single().AvailableCopies.Should().Be(1);
    }

    [Fact]
    public async Task Catalog_excludes_deleted_books_and_deleted_copies_from_stock()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var active = new Book { TenantId = 101, Title = "Active" };
        var deleted = new Book { TenantId = 101, Title = "Deleted", IsDeleted = true };
        db.Books.AddRange(active, deleted);
        await db.SaveChangesAsync();
        db.BookCopies.AddRange(
            new BookCopy { TenantId = 101, BookId = active.Id, AccessionNumber = "ACTIVE" },
            new BookCopy { TenantId = 101, BookId = active.Id, AccessionNumber = "DELETED", IsDeleted = true });
        await db.SaveChangesAsync();
        var result = await Service(db, 101).GetCatalogAsync(null, 1, 20);
        result.Data!.TotalCount.Should().Be(1);
        result.Data.Items.Single().TotalCopies.Should().Be(1);
        result.Data.Items.Single().AvailableCopies.Should().Be(1);
    }

    [Fact]
    public async Task Deleted_student_or_copy_cannot_start_new_circulation()
    {
        var options = Options();
        await using var db = Context(options, 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        copy.IsDeleted = true;
        await db.SaveChangesAsync();
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).StatusCode.Should().Be(409);
        copy.IsDeleted = false;
        student.IsDeleted = true;
        await db.SaveChangesAsync();
        (await service.IssueAsync(Request(student, copy, Guid.NewGuid()))).StatusCode.Should().Be(404);
        (await db.BookIssues.CountAsync()).Should().Be(0);
    }


    [Fact]
    public async Task Direct_issue_cannot_bypass_active_reservation_for_same_campus()
    {
        await using var db = Context(Options(), 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var waiting = new Student { TenantId = 101, PersonId = 22, StudentCode = "WAIT-1",
            FullName = "Waiting learner", StatusCode = "Active" };
        db.Students.Add(waiting);
        await db.SaveChangesAsync();
        db.StudentEnrollments.Add(new StudentEnrollment { TenantId = 101, StudentId = waiting.Id,
            CampusId = 0, IsCurrent = true, State = EnrollmentState.Active, RollNo = "22",
            EnrollmentDate = new DateOnly(2026, 1, 1) });
        db.BookReservations.Add(new BookReservation { TenantId = 101, BookId = copy.BookId,
            StudentId = waiting.Id, ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
            ReservedAt = DateTime.UtcNow.AddMinutes(-5), ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var result = await Service(db, 101).IssueAsync(Request(student, copy, Guid.NewGuid()));
        result.StatusCode.Should().Be(409);
        result.Message.Should().Contain("reservation");
        (await db.BookIssues.CountAsync()).Should().Be(0);
        copy.State.Should().Be(BookCopyState.Available);
    }

    [Fact]
    public async Task Direct_issue_respects_campus_queue_and_ignores_expired_or_cancelled_reservations()
    {
        await using var db = Context(Options(), 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var enrollment = await db.StudentEnrollments.SingleAsync();
        enrollment.CampusId = 11;
        var branch = new LibraryBranch { TenantId = 101, CampusId = 11, Code = "N",
            Name = "North", IsActive = true };
        var other = new Student { TenantId = 101, PersonId = 23, StudentCode = "WAIT-2",
            FullName = "South learner", StatusCode = "Active" };
        db.AddRange(branch, other);
        await db.SaveChangesAsync();
        copy.LibraryBranchId = branch.Id;
        db.StudentEnrollments.Add(new StudentEnrollment { TenantId = 101, StudentId = other.Id,
            CampusId = 22, IsCurrent = true, State = EnrollmentState.Active, RollNo = "23",
            EnrollmentDate = new DateOnly(2026, 1, 1) });
        db.BookReservations.AddRange(
            new BookReservation { TenantId = 101, BookId = copy.BookId, StudentId = other.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
                ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new BookReservation { TenantId = 101, BookId = copy.BookId, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Pending,
                ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
            new BookReservation { TenantId = 101, BookId = copy.BookId, StudentId = student.Id,
                ClientRequestId = Guid.NewGuid(), State = BookReservationState.Cancelled,
                ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();

        var result = await Service(db, 101).IssueAsync(Request(student, copy, Guid.NewGuid()));
        result.Success.Should().BeTrue(result.Message);
        (await db.BookIssues.CountAsync()).Should().Be(1);
        copy.State.Should().Be(BookCopyState.Issued);
    }

    [Fact]
    public async Task Direct_issue_idempotent_replay_remains_valid_after_new_reservation()
    {
        await using var db = Context(Options(), 101);
        var (student, copy) = await SeedBorrower(db, 101, true);
        var service = Service(db, 101);
        var request = Request(student, copy, Guid.NewGuid());
        var issued = await service.IssueAsync(request);
        issued.Success.Should().BeTrue(issued.Message);
        db.BookReservations.Add(new BookReservation { TenantId = 101, BookId = copy.BookId,
            StudentId = student.Id, ClientRequestId = Guid.NewGuid(),
            State = BookReservationState.Pending, ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var replay = await service.IssueAsync(request);
        replay.Success.Should().BeTrue(replay.Message);
        replay.Data!.Reference.Should().Be(issued.Data!.Reference);
        (await db.BookIssues.CountAsync()).Should().Be(1);
    }

    private static IssueBookRequestDto Request(Student student, BookCopy copy, Guid key) => new()
    {
        ClientRequestId = key, BookCopyId = copy.Id, StudentReference = student.PublicId,
        IssueDate = new DateOnly(2026, 10, 10), DueDate = new DateOnly(2026, 10, 20)
    };

    private static async Task<(Student Student, BookCopy Copy)> SeedBorrower(EduOSDbContext db, long tenant, bool active)
    {
        var student = new Student { TenantId = tenant, PersonId = 1, StudentCode = "S-" + tenant, FullName = "Learner" };
        var book = new Book { TenantId = tenant, Title = "Test Book" };
        db.AddRange(student, book); await db.SaveChangesAsync();
        var copy = new BookCopy { TenantId = tenant, BookId = book.Id, AccessionNumber = "A-" + tenant };
        db.BookCopies.Add(copy);
        if (active) db.StudentEnrollments.Add(new StudentEnrollment
        {
            TenantId = tenant, StudentId = student.Id, IsCurrent = true, State = EnrollmentState.Active,
            RollNo = "1", EnrollmentDate = new DateOnly(2026, 1, 1)
        });
        await db.SaveChangesAsync();
        return (student, copy);
    }

    private static LibraryService Service(EduOSDbContext db, long tenant, string role = "TenantAdmin") => new(
        new GenericRepository<Book>(db), new GenericRepository<BookCopy>(db),
        new GenericRepository<BookIssue>(db), new GenericRepository<BookCategory>(db),
        new GenericRepository<Student>(db), new GenericRepository<StudentEnrollment>(db),
        db, new TestUser(tenant, role), TimeProvider.System, NullLogger<LibraryService>.Instance,
        new GenericRepository<LibraryBranch>(db),
        new GenericRepository<Employee>(db),
        new GenericRepository<EmployeeCampusAssignment>(db), null, null,
        new GenericRepository<BookReservation>(db));

    private static DbContextOptions<EduOSDbContext> Options() => new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase("library-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant, string role = "TenantAdmin")
    {
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, "7"), new Claim(ClaimTypes.Role, role), new Claim("TenantId", tenant.ToString())
        ], "Test")) };
        http.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = http });
    }

    private sealed class TestUser(long tenant, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long UserId => 7;
        public long TenantId => tenant;
        public string? FullName => "Test";
        public string? Email => null;
        public bool IsSuperAdmin => false;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string value) => role == value;
        public string? IpAddress => null;
        public string? UserAgent => null;
    }
}
