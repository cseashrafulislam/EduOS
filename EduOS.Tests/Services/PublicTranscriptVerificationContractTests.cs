using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class PublicTranscriptVerificationContractTests
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
    public void Public_transcript_lookup_is_scoped_and_never_returns_private_fields()
    {
        var service = Read("EduOS.Service", "Services", "Assessment",
            "PublicCertificateVerificationService.cs");
        var api = Read("EduOS.App", "Controllers", "Api",
            "PublicCertificateVerificationApiController.cs");
        var dto = Read("EduOS.Core", "DTOs", "Assessment",
            "PublicTranscriptVerificationDto.cs");
        service.Should().Contain("VerifyTranscriptAsync")
            .And.Contain("student.TenantId == tenant.Id")
            .And.Contain("newer.VersionNo > publication.VersionNo")
            .And.Contain("transcript.IsRevoked ? \"Revoked\"");
        api.Should().Contain("[EnableRateLimiting(\"AdmissionIntakePolicy\")]")
            .And.Contain("transcripts/{reference:guid}");
        dto.Should().NotContain("StudentName").And.NotContain("StudentId")
            .And.NotContain("EnrollmentId").And.NotContain("Guardian")
            .And.NotContain("GradePoint").And.NotContain("ObtainedMarks");
    }
}
