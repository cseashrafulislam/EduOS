using EduOS.App.Controllers.Api;
using EduOS.Service.Services.Platform;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AlumniInvitationContractTests
{
    [Theory]
    [InlineData(nameof(AlumniApiController.InvitationSummary))]
    [InlineData(nameof(AlumniApiController.SendInvitations))]
    public void Invitation_endpoints_require_tenant_admin(string method)
    {
        var target = typeof(AlumniApiController).GetMethod(method)!;
        Assert.Contains(target.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            attr => ((AuthorizeAttribute)attr).Roles == "TenantAdmin");
    }

    [Fact]
    public void Dispatch_is_rate_limited_and_uses_csrf_enabled_controller()
    {
        Assert.Contains(typeof(AlumniApiController).GetMethod(nameof(AlumniApiController.SendInvitations))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), true), x => x != null);
        Assert.Contains("AutoValidateAntiforgeryToken", Read("EduOS.App", "Controllers", "Api", "AlumniApiController.cs"));
    }

    [Fact]
    public void External_channel_consent_is_separate_and_gateway_is_tenant_owned()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniInvitationsService.cs");
        Assert.Contains("p.InvitationEmailOptIn = 1", source);
        Assert.Contains("p.InvitationSmsOptIn = 1", source);
        Assert.DoesNotContain("p.DirectoryOptIn = 1", source);
        Assert.Contains("CampusGrantSql", source);
        Assert.Contains("x.FeesCleared = 1", source);
        Assert.Contains("en.CampusId = p.CampusId", source);
        Assert.Contains("x.IsDeleted = 0", source);
        Assert.Contains("x.IsDefault && x.IsActive", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("request.Confirmed", source);
        Assert.Contains("InvitationBatchLimit = 5", source);
        Assert.Contains("!reserved.Replay", source);
        Assert.Contains("CONSENT_OR_EVENT_REVOKED", source);
        Assert.DoesNotContain("EmailSettings", source);
        Assert.DoesNotContain("SmsSettings", source);
    }

    [Fact]
    public void Ledger_enforces_one_delivery_attempt_and_no_contact_retention()
    {
        var migration = Read("EduOS.Persistence", "Migrations", "20261101170000_AlumniInvitationDispatch.cs");
        Assert.Contains("UX_AlumniInvitationAttempts_Recipient", migration);
        Assert.Contains("UX_AlumniInvitationBatches_Request", migration);
        Assert.Contains("FK_AlumniInvitationAttempts_Profile", migration);
        Assert.Contains("FK_AlumniInvitationAttempts_Batch", migration);
        Assert.DoesNotContain("ContactEmail", migration);
        Assert.DoesNotContain("ContactPhone", migration);
        Assert.DoesNotContain("Destination", migration);
        Assert.Contains("dispatch evidence must be retained", migration);
    }

    [Fact]
    public void External_transport_pins_public_dns_disables_redirects_and_does_not_retry()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniInvitationTransport.cs");
        Assert.Contains("SecureWebhookTransport.IsPublicAddress", source);
        Assert.Contains("AllowAutoRedirect = false", source);
        Assert.Contains("UseProxy = false", source);
        Assert.Contains("ConnectPublicAsync", source);
        Assert.Contains("GatewayCredentials.v1", source);
        Assert.Contains("SecureSocketOptions.StartTls", source);
        Assert.Contains("PROVIDER_HTTP_", source);
        Assert.True(SecureWebhookTransport.IsPublicAddress(System.Net.IPAddress.Parse("8.8.8.8")));
        Assert.False(SecureWebhookTransport.IsPublicAddress(System.Net.IPAddress.Parse("127.0.0.1")));
        Assert.False(SecureWebhookTransport.IsPublicAddress(System.Net.IPAddress.Parse("10.0.0.1")));
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
