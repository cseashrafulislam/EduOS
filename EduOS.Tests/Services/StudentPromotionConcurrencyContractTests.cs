using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class StudentPromotionConcurrencyContractTests
{
    [Fact]
    public void Promote_keeps_roll_and_capacity_checks_inside_batch_locked_transaction()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Students", "StudentPromotionService.cs"));
        var promoteStart = source.IndexOf("Task<ApiResponse<StudentPromotionResultDto>> PromoteAsync", StringComparison.Ordinal);
        var historyStart = source.IndexOf("Task<ApiResponse<IReadOnlyList<StudentPromotionHistoryDto>>> GetHistoryAsync", StringComparison.Ordinal);

        promoteStart.Should().BeGreaterThanOrEqualTo(0);
        historyStart.Should().BeGreaterThan(promoteStart);
        var promoteMethod = source[promoteStart..historyStart];

        promoteMethod.Should().Contain("System.Data.IsolationLevel.ReadCommitted");
        promoteMethod.Should().Contain("_uow.ExecuteInTransactionAsync(async token =>");
        promoteMethod.Should().Contain("Target roll already belongs to an active student.");
        promoteMethod.Should().Contain("if (batch.Capacity <= 0 || await _enrollments.GetQueryable().AsNoTracking().CountAsync");
        promoteMethod.Should().Contain("System.Data.IsolationLevel.ReadCommitted, ct);");
        promoteMethod.Should().Contain("throw new BatchAllocationException(\"Target batch is closed or has reached capacity.\")");
        promoteMethod.Should().Contain("catch (BatchAllocationException ex)");
        promoteMethod.Should().Contain("_batches.Update(batch);");
        promoteMethod.IndexOf("_batches.Update(batch);", StringComparison.Ordinal)
            .Should().BeLessThan(promoteMethod.IndexOf("if (batch.Capacity <= 0", StringComparison.Ordinal));
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
        promoteMethod.Should().Contain("Concurrent progression detected. Reload and retry.");
    }

    [Fact]
    public void Promotion_replay_checks_source_enrollment_and_audit_note()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Students", "StudentPromotionService.cs"));
        source.Should().Contain("replayResult.Data.FromEnrollmentId != request.SourceEnrollmentId");
        source.Should().Contain("replay.Note?.Trim()");
        source.Should().Contain("request.Note?.Trim()");
        source.Should().Contain("Client request ID is already used for different progression details.");
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
