using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class LibraryReturnContractTests
{
    [Fact]
    public void Return_replay_is_only_idempotent_for_returned_issues_with_matching_date()
    {
        var source = ReadSource("EduOS.Service", "Services", "Library", "LibraryService.cs");
        var start = source.IndexOf("public async Task<ApiResponse<BookIssueDto>> CloseAsync", StringComparison.Ordinal);
        var end = source.IndexOf("public async Task<ApiResponse<PagedResult<BookIssueDto>>> GetMyIssuesAsync", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);
        var method = source[start..end];
        method.Should().Contain("if (issue.State == BookIssueState.Returned)");
        method.Should().Contain("request.ReturnDate != issue.ReturnDate");
        method.Should().Contain("if (issue.State != BookIssueState.Issued)");
        method.Should().Contain("Only issued copies may be returned.");
    }

    private static string ReadSource(params string[] segments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(new[] { dir.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Library service source not found.");
    }
}
