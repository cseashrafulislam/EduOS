using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class StudentPromotionConcurrencyContractTests
{
    [Fact]
    public void Promote_keeps_roll_and_capacity_checks_inside_serializable_transaction()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Students", "StudentPromotionService.cs"));
        var promoteStart = source.IndexOf("Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync", StringComparison.Ordinal);
        var historyStart = source.IndexOf("Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync", StringComparison.Ordinal);

        promoteStart.Should().BeGreaterThanOrEqualTo(0);
        historyStart.Should().BeGreaterThan(promoteStart);
        var promoteMethod = source[promoteStart..historyStart];

        promoteMethod.Should().Contain("IsolationLevel = IsolationLevel.Serializable");
        promoteMethod.Should().Contain("TransactionScopeAsyncFlowOption.Enabled");
        promoteMethod.Should().Contain("Target roll is already assigned in the section.");
        promoteMethod.Should().Contain("if (occupied >= targetSection.Capacity)");
        promoteMethod.Should().Contain("scope.Complete();");
    }

    [Fact]
    public void Promote_maps_database_and_serialization_conflicts_to_http_409()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Students", "StudentPromotionService.cs"));
        var promoteStart = source.IndexOf("Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync", StringComparison.Ordinal);
        var historyStart = source.IndexOf("Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync", StringComparison.Ordinal);
        var promoteMethod = source[promoteStart..historyStart];

        promoteMethod.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        promoteMethod.Should().Contain("catch (DbUpdateException ex)");
        promoteMethod.Should().Contain("catch (TransactionAbortedException ex)");
        promoteMethod.Should().Contain("Reload and try again.\", 409");
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
