using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace EduOS.Tests.Services;
public sealed partial class LibraryBranchServiceTests
{
    [Fact]
    public async Task Branch_with_active_copy_cannot_be_deactivated()
    {
        await using var db = Context(101);
        var campus = new Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        db.Campuses.Add(campus); await db.SaveChangesAsync();
        var branch = new LibraryBranch { TenantId = 101, CampusId = campus.Id,
            Name = "Central", Code = "LIB", RowVersion = [1, 2, 3] };
        db.LibraryBranches.Add(branch); await db.SaveChangesAsync();
        var book = new Book { TenantId = 101, Title = "Math" };
        db.Books.Add(book); await db.SaveChangesAsync();
        db.BookCopies.Add(new BookCopy { TenantId = 101, BookId = book.Id,
            LibraryBranchId = branch.Id, AccessionNumber = "ACC-1" });
        await db.SaveChangesAsync();
        var request = new SaveLibraryBranchRequestDto { CampusId = campus.Id, Name = branch.Name,
            Code = branch.Code, IsActive = false, RowVersion = Convert.ToBase64String(branch.RowVersion) };
        (await Service(db, 101).SaveBranchAsync(branch.Id, request)).StatusCode.Should().Be(409);
    }
}