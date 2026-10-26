using EduOS.Core.DTOs.Auth;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.App;

public sealed class UnscopedIdentityRoleContractTests
{
    [Fact]
    public void Tenant_user_role_payloads_use_unscoped_identity_names()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var member = JsonSerializer.Serialize(new TenantUserListItemDto
        {
            UnscopedIdentityRoleNames = ["Teacher"],
            CanRemoveUnscopedIdentityRoles = true
        }, options);
        Assert.Contains("\"unscopedIdentityRoleNames\"", member);
        Assert.Contains("\"canRemoveUnscopedIdentityRoles\"", member);
        Assert.DoesNotContain("legacyGlobalRole", member, StringComparison.OrdinalIgnoreCase);

        var request = JsonSerializer.Serialize(new TenantMemberRolesRequestDto
        {
            ConfirmUnscopedIdentityRoleRemoval = true
        }, options);
        Assert.Contains("\"confirmUnscopedIdentityRoleRemoval\":true", request);
        Assert.DoesNotContain("confirmLegacyGlobalRoleRemoval", request, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tenant_user_editor_consumes_the_canonical_identity_role_contract()
    {
        var script = Read("EduOS.App", "wwwroot", "js", "tenant-users", "index.js");
        var view = Read("EduOS.App", "Views", "TenantUsers", "Index.cshtml");
        Assert.Contains("member.unscopedIdentityRoleNames", script);
        Assert.Contains("member.canRemoveUnscopedIdentityRoles", script);
        Assert.Contains("confirmUnscopedIdentityRoleRemoval", script);
        Assert.Contains("memberUnscopedIdentityRoleApproval", script);
        Assert.Contains("memberUnscopedIdentityRoleApproval", view);
        Assert.DoesNotContain("legacyGlobalRoleNames", script);
        Assert.DoesNotContain("memberLegacyRole", view);
    }

    private static string Read(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
