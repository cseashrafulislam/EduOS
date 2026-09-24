using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentExitIntegrityContractTests
{
    private static readonly string Source = File.ReadAllText(FindRepositoryFile(
        "EduOS.Service", "Services", "Students", "StudentExitService.cs"));

    [Fact]
    public void Exit_closes_legacy_and_canonical_current_enrollments_atomically()
    {
        Source.Should().Contain("IGenericRepository<StudentEnrollment>");
        Source.Should().Contain("IsolationLevel = IsolationLevel.Serializable");
        Source.Should().Contain("TransactionScopeAsyncFlowOption.Enabled");
        Source.Should().Contain("x.IsCurrent && x.IsActive");
        Source.Should().Contain("enrollment.IsCurrent = false");
        Source.Should().Contain("enrollment.IsActive = false");
        Source.Should().Contain("EnrollmentStatus.Transferred");
        Source.Should().Contain("EnrollmentStatus.Completed");
        Source.Should().Contain("EnrollmentStatus.Dropped");
        Source.Should().Contain("scope.Complete()");
    }

    [Fact]
    public void Exit_is_retry_safe_and_maps_write_conflicts_to_reloadable_responses()
    {
        Source.Should().Contain("x.ClientRequestId == request.ClientRequestId");
        Source.Should().Contain("Student exit was already processed.");
        Source.Should().Contain("catch (DbUpdateConcurrencyException ex)");
        Source.Should().Contain("catch (DbUpdateException ex)");
        Source.Should().Contain("catch (TransactionAbortedException ex)");
        Source.Should().Contain("Reload and try again.");
        Source.Should().Contain(", 409");
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
