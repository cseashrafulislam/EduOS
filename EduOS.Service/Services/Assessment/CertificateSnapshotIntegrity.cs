using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Separate encryption purpose prevents swapping a transcript payload for a certificate.</summary>
public static class CertificateSnapshotIntegrity
{
    public const string Purpose = "EduOS.Assessment.CertificateSnapshot.v1";

    public static bool TryRead(IDataProtector? protector, CertificateIssue issue,
        out CertificateSnapshotDocumentDto? document)
    {
        document = null;
        if (protector == null || string.IsNullOrWhiteSpace(issue.ProtectedDocument)) return false;
        try
        {
            var json = protector.Unprotect(issue.ProtectedDocument);
            if (json.Length > 131072) return false;
            var value = JsonSerializer.Deserialize<CertificateSnapshotDocumentDto>(json);
            if (value == null || value.SchemaVersion != 1 || value.TenantId != issue.TenantId ||
                value.StudentId != issue.StudentId || value.StudentEnrollmentId != issue.StudentEnrollmentId ||
                value.CertificateTemplateId != issue.CertificateTemplateId ||
                value.CertificateReference != issue.PublicId || value.CertificateNumber != issue.CertificateNumber ||
                value.IssueDate != issue.IssueDate || value.IssuedByUserId != issue.IssuedByUserId ||
                value.StudentReference == Guid.Empty || value.StudentEnrollmentReference == Guid.Empty ||
                value.ResultPublicationId <= 0 || value.ResultPublicationVersionNo <= 0 ||
                string.IsNullOrWhiteSpace(value.IssuerName) || value.IssuerName.Length > 300 ||
                string.IsNullOrWhiteSpace(value.StudentName) || value.StudentName.Length > 200 ||
                string.IsNullOrWhiteSpace(value.StudentCode) || value.StudentCode.Length > 50 ||
                string.IsNullOrWhiteSpace(value.CampusName) || value.CampusName.Length > 150 ||
                string.IsNullOrWhiteSpace(value.BatchName) || value.BatchName.Length > 150 ||
                string.IsNullOrWhiteSpace(value.TemplateName) || value.TemplateName.Length > 150 ||
                string.IsNullOrWhiteSpace(value.RenderedBody) || value.RenderedBody.Length > 65000)
                return false;
            document = value;
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (FormatException) { return false; }
        catch (JsonException) { return false; }
    }
}
