using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class StudentDirectoryUiContractTests
{
    [Fact]
    public void Student_directory_uses_canonical_roll_level_and_batch_contracts()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "wwwroot", "js", "students", "index.js"));
        source.Should().Contain("line(i18n.roll, item.rollNo)");
        source.Should().Contain("item.academicLevelName");
        source.Should().Contain("item.academicBatchName");
        source.Should().Contain("x.academicLevelName");
        source.Should().Contain("x.academicBatchName");
        source.Should().Contain("x.rollNo");
        source.Should().Contain("data-student-promotion-link");
        source.Should().Contain("studentReference=");
        source.Should().NotContain("line(i18n.roll, item.roll)");
        source.Should().NotContain("x.academicUnit, x.section, x.group, x.roll");
    }

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(segments)}");
    }
}
