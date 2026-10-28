using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PublicCertificateVerificationContractTests
{
    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", parts));
    }

    [Fact]
    public void Public_lookup_requires_random_reference_and_never_returns_private_learner_fields()
    {
        var service = Read("EduOS.Service", "Services", "Assessment", "PublicCertificateVerificationService.cs");
        var dto = Read("EduOS.Core", "DTOs", "Assessment", "PublicCertificateVerificationDto.cs");
        service.Should().Contain("reference == Guid.Empty");
        service.Should().Contain(".Take(2).ToListAsync(cancellationToken)");
        service.Should().Contain("matches.Count != 1");
        service.Should().Contain("enrollment.StudentId == certificate.StudentId");
        service.Should().Contain("enrollment.TenantId == tenant.Id");
        service.Should().Contain("summary.PublicationVersionNo == publication.VersionNo");
        service.Should().Contain("publication.State == ResultPublicationState.Published");
        service.Should().Contain("certificate.IsRevoked ? \"Revoked\"");
        service.Should().Contain("module.Code == \"EXAM\"");
        dto.Should().NotContain("StudentName").And.NotContain("StudentId")
            .And.NotContain("EnrollmentId").And.NotContain("Guardian");
    }

    [Fact]
    public void Public_route_is_rate_limited_uncached_and_does_not_reuse_authenticated_admin_endpoint()
    {
        var api = Read("EduOS.App", "Controllers", "Api", "PublicCertificateVerificationApiController.cs");
        var mvc = Read("EduOS.App", "Controllers", "CertificateVerificationController.cs");
        var di = Read("EduOS.App", "Extensions", "ApplicationServiceExtensions.cs");
        api.Should().Contain("[AllowAnonymous]").And.Contain("[EnableRateLimiting(\"AdmissionIntakePolicy\")]");
        api.Should().Contain("[ResponseCache(NoStore = true");
        mvc.Should().Contain("/verify/certificate/{reference:guid}");
        di.Should().Contain("IPublicCertificateVerificationService, PublicCertificateVerificationService");
    }
}
