using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Entities.Students;
using EduOS.Persistence.Repositories;
using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Architecture;

public sealed class RepositorySurfaceContractTests
{
    [Fact]
    public void Generic_repository_exposes_only_bounded_query_and_explicit_write_operations()
    {
        var retired = new[] { "GetAllAsync", "FindAsync", "FindFirstOrDefaultAsync", "Delete", "DeleteRange", "DeleteByIdAsync", "ExistsAsync" };
        foreach (var type in new[] { typeof(IGenericRepository<Student>), typeof(GenericRepository<Student>) })
        {
            var names = type.GetMethods().Select(x => x.Name).ToArray();
            names.Intersect(retired).Should().BeEmpty(
                $"{type.Name} must not reintroduce unbounded queries or unreviewed hard deletes");
            names.Should().Contain("GetPagedAsync");
            names.Should().Contain("GetQueryable");
        }
    }
}
