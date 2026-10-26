using EduOS.Core.DTOs.Hostel;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class HostelAllocationTransferLineageReadbackTests
{
    [Fact]
    public void Lineage_readback_exposes_only_flags_not_internal_links_or_financial_data()
    {
        var dto = new HostelAllocationHistoryRowDto
        {
            IsTransferSuccessor = true,
            HasTransferSuccessor = true
        };
        var json = JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"isTransferSuccessor\":true", json);
        Assert.Contains("\"hasTransferSuccessor\":true", json);
        Assert.DoesNotContain("previousAllocationId", json);
        Assert.DoesNotContain("successorAllocationId", json);
        Assert.DoesNotContain("monthlyRent", json);
        Assert.DoesNotContain("securityDeposit", json);
    }

    [Fact]
    public void Successor_lookup_is_tenant_scoped_paged_and_campus_bound()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "EduOS.Service", "Services", "Hostel", "HostelService.AllocationHistory.cs"));
        Assert.Contains("pageIds.Contains(successor.PreviousAllocationId.Value)", source);
        Assert.Contains("successor.TenantId == tenant && successorBed.TenantId == tenant", source);
        Assert.Contains("successorRoom.TenantId == tenant && successorHostel.TenantId == tenant", source);
        Assert.Contains("IgnoreQueryFilters()", source);
        Assert.Contains("successorCampusLookup[x.Id].Contains(x.CampusId)", source);
        Assert.Contains("AllowedCampusIds(tenant)", source);
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
        throw new FileNotFoundException("Hostel allocation history service file was not found.");
    }
}
