using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class CertificateSnapshotIntegrityTests
{
    [Fact]
    public void Sealed_certificate_roundtrips_and_rejects_cross_tenant_swap_and_modified_document()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(CertificateSnapshotIntegrity.Purpose);
        var issue = new CertificateIssue
        {
            PublicId = Guid.NewGuid(), TenantId = 17, StudentId = 3, StudentEnrollmentId = 4,
            CertificateTemplateId = 5, CertificateNumber = "CERT-SECURE", IssuedByUserId = 8,
            IssueDate = new DateOnly(2026, 10, 29)
        };
        var document = new CertificateSnapshotDocumentDto
        {
            TenantId = issue.TenantId, StudentId = issue.StudentId, StudentReference = Guid.NewGuid(),
            StudentEnrollmentId = issue.StudentEnrollmentId, StudentEnrollmentReference = Guid.NewGuid(),
            CertificateTemplateId = issue.CertificateTemplateId, CertificateReference = issue.PublicId,
            CertificateNumber = issue.CertificateNumber, IssuedByUserId = issue.IssuedByUserId,
            IssueDate = issue.IssueDate, IssuedAtUtc = DateTime.UtcNow, IssuerName = "Example Institute",
            StudentName = "Student A", StudentCode = "ST-101", CampusName = "Main",
            BatchName = "Batch 1", TemplateName = "Achievement", RenderedBody = "Certificate body",
            ResultPublicationId = 20, ResultPublicationVersionNo = 1
        };
        issue.ProtectedDocument = protector.Protect(JsonSerializer.Serialize(document));
        CertificateSnapshotIntegrity.TryRead(protector, issue, out var restored).Should().BeTrue();
        restored!.StudentName.Should().Be("Student A");
        issue.TenantId = 99;
        CertificateSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        issue.TenantId = 17;
        issue.CertificateNumber = "SWAPPED";
        CertificateSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        issue.CertificateNumber = "CERT-SECURE";
        issue.ProtectedDocument += "corrupted";
        CertificateSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        CertificateSnapshotIntegrity.TryRead(null, issue, out _).Should().BeFalse();
    }

    [Fact]
    public void Foreign_key_ring_and_legacy_certificate_have_no_formal_document()
    {
        var protector = new EphemeralDataProtectionProvider().CreateProtector(CertificateSnapshotIntegrity.Purpose);
        var foreign = new EphemeralDataProtectionProvider().CreateProtector(CertificateSnapshotIntegrity.Purpose);
        var issue = new CertificateIssue { ProtectedDocument = protector.Protect("{}") };
        CertificateSnapshotIntegrity.TryRead(foreign, issue, out _).Should().BeFalse();
        issue.ProtectedDocument = null;
        CertificateSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
    }
}
