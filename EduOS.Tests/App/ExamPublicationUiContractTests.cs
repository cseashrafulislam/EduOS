using FluentAssertions;
using Xunit;

namespace EduOS.Tests.App;

public sealed class ExamPublicationUiContractTests
{
    [Theory]
    [InlineData("TenantAdmin")]
    [InlineData("Principal")]
    [InlineData("VicePrincipal")]
    [InlineData("ExamController")]
    public void Exam_publication_view_exposes_all_authorized_roles(string role)
    {
        var view = ReadFile("EduOS.App", "Views", "ExamOperations", "Index.cshtml");
        view.Should().Contain($"User.IsInRole(\"{role}\")");
        view.Should().Contain("data-can-publish");
    }

    private static string ReadFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Cannot locate exam publication view.");
    }
}
