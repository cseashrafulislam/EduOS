using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class TransportConcurrencyContractTests
{
    [Fact]
    public void Assign_keeps_capacity_check_inside_serializable_transaction()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Transport", "TransportService.cs"));
        var assignStart = source.IndexOf("Task<ApiResponse<StudentTransportDto>> AssignAsync", StringComparison.Ordinal);
        var closeStart = source.IndexOf("Task<ApiResponse<StudentTransportDto>> CloseAsync", StringComparison.Ordinal);

        assignStart.Should().BeGreaterThanOrEqualTo(0);
        closeStart.Should().BeGreaterThan(assignStart);
        var assignMethod = source[assignStart..closeStart];

        assignMethod.Should().Contain("_unitOfWork.ExecuteInTransactionAsync");
        assignMethod.Should().Contain("System.Data.IsolationLevel.Serializable");
        assignMethod.Should().NotContain("new TransactionScope");
        assignMethod.Should().Contain("CountAsync(x => x.TenantId == tenant &&");
        assignMethod.Should().Contain("if (occupied >= vehicle.Capacity)");
        assignMethod.Should().Contain("await _unitOfWork.SaveChangesAsync(ct)");
        assignMethod.Should().Contain("x.State == TransportAssignmentState.Active");
        assignMethod.Should().Contain("Client request ID was reused for different transport assignment data.");
        assignMethod.Should().Contain("replay.RouteReference != request.RouteReference");
    }

    [Fact]
    public void Assign_maps_database_and_serialization_conflicts_to_http_409()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Transport", "TransportService.cs"));
        var assignStart = source.IndexOf("Task<ApiResponse<StudentTransportDto>> AssignAsync", StringComparison.Ordinal);
        var closeStart = source.IndexOf("Task<ApiResponse<StudentTransportDto>> CloseAsync", StringComparison.Ordinal);
        var assignMethod = source[assignStart..closeStart];

        assignMethod.Should().Contain("catch (DbUpdateException ex)");
        assignMethod.Should().Contain("catch (TransactionAbortedException ex)");
        assignMethod.Should().Contain("catch (Microsoft.Data.SqlClient.SqlException ex) when (ex.Number is 1205 or 1222 or 3960)");
        assignMethod.Should().Contain("Transport assignment conflicts with another update.\", 409");
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
