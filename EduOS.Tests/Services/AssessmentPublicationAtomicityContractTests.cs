using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentPublicationAtomicityContractTests
{
    [Fact]
    public void Publication_validates_entire_roster_before_any_publication_write()
    {
        var source = ReadRepositoryFile("EduOS.Service", "Services", "Exams", "ExamWorkflowService.cs");
        var start = source.IndexOf("public async Task<ApiResponse<ResultPublicationDto>> PublishResultAsync", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task<(AssessmentResultSheetDto? Sheet, string? Error)> CalculateAsync", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1);
        end.Should().BeGreaterThan(start);
        var method = source[start..end];
        var preflight = method.IndexOf("if (calculated.Sheet!.Results.Any(result => !enrolled.ContainsKey(result.RollNo)))", StringComparison.Ordinal);
        var persist = method.IndexOf("await _publications.AddAsync(publication)", StringComparison.Ordinal);
        preflight.Should().BeGreaterThan(-1);
        persist.Should().BeGreaterThan(preflight);
        method[persist..].Should().NotContain("return Error<ResultPublicationDto>(\"Student enrollment roster changed.");
    }

    private static string ReadRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Assessment service source not found.");
    }
}
