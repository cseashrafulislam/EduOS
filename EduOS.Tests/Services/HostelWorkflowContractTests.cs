using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public class HostelWorkflowContractTests
{
    [Fact]
    public void Hostel_controller_requires_auth_module_and_privileged_mutations()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.App", "Controllers", "Api", "HostelController.cs"));
        source.Should().Contain("[Authorize]");
        source.Should().Contain("[RequireModule(\"HOSTEL\")]");
        source.Should().Contain("[AutoValidateAntiforgeryToken]");
        source.Should().Contain("TenantAdmin,Principal,HostelWarden");
        source.Should().NotContain("AllowAnonymous");
    }

    [Fact]
    public void Allocation_keeps_capacity_and_duplicate_checks_serialized_and_tenant_scoped()
    {
        var source = File.ReadAllText(FindRepositoryFile("EduOS.Service", "Services", "Hostel", "HostelService.cs"));
        source.Should().Contain("IsolationLevel = IsolationLevel.Serializable");
        source.Should().Contain("TransactionScopeAsyncFlowOption.Enabled");
        source.Should().Contain("x.TenantId == tenant &&");
        source.Should().Contain("roomBedIds.Contains(x.HostelBedId)");
        source.Should().Contain("if (occupied >= selection.Room.Capacity)");
        source.Should().Contain("scope.Complete();");
        source.Should().Contain("x.StudentId == student.Id && x.State == HostelAllocationState.Active");
        source.Should().Contain("Client request ID was reused for different allocation data.");
        source.Should().Contain("catch (TransactionAbortedException ex)");
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
