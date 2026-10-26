using EduOS.Core.DTOs.Library;
using EduOS.Core.Entities.SaaS;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace EduOS.Tests.Services;
public sealed partial class LibraryBranchServiceTests
{
    [Fact]
    public async Task Foreign_campus_and_student_cannot_create_branch()
    {
        await using var db = Context(101);
        var campus = new Campus { TenantId = 202, Name = "Other", Code = "OTH" };
        db.Campuses.Add(campus); await TestTenantSeeder.SaveAsync(db);
        var request = new SaveLibraryBranchRequestDto { CampusId = campus.Id, Name = "Central", Code = "LIB" };
        (await Service(db, 101, "Student").SaveBranchAsync(null, request)).StatusCode.Should().Be(403);
        (await Service(db, 101).SaveBranchAsync(null, request)).StatusCode.Should().Be(404);
        (await db.LibraryBranches.CountAsync()).Should().Be(0);
    }
    [Fact]
    public async Task Branch_create_replays_same_payload_but_rejects_conflict()
    {
        await using var db = Context(101);
        var campus = new Campus { TenantId = 101, Name = "Main", Code = "MAIN" };
        db.Campuses.Add(campus); await db.SaveChangesAsync();
        var service = Service(db, 101);
        var request = new SaveLibraryBranchRequestDto { CampusId = campus.Id, Name = "Central", Code = " LIB " };
        var first = await service.SaveBranchAsync(null, request);
        first.Success.Should().BeTrue();
        (await service.SaveBranchAsync(null, request)).Data!.Id.Should().Be(first.Data!.Id);
        (await service.SaveBranchAsync(null, new SaveLibraryBranchRequestDto {
            CampusId = campus.Id, Name = "Different", Code = "LIB"
        })).StatusCode.Should().Be(409);
        (await service.GetBranchesAsync(campus.Id)).Data.Should().ContainSingle();
    }
}
