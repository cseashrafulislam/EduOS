using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using Microsoft.AspNetCore.DataProtection;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.AssessmentAdministration;

/// <summary>Authenticates immutable issue-time evidence using persistent Data Protection keys.</summary>
public static class TranscriptSnapshotIntegrity
{
    public const string Purpose = "EduOS.Assessment.TranscriptSnapshot.v1";

    public static bool TryRead(IDataProtector? protector, TranscriptIssue issue,
        out TranscriptSnapshotDocumentDto? snapshot)
    {
        snapshot = null;
        if (protector == null || string.IsNullOrWhiteSpace(issue.ProtectedAcademicRecord)) return false;
        try
        {
            var serialized = protector.Unprotect(issue.ProtectedAcademicRecord);
            if (serialized.Length > 131072) return false;
            var record = JsonSerializer.Deserialize<TranscriptSnapshotDocumentDto>(serialized);
            if (record == null || record.SchemaVersion != 1 || record.TenantId != issue.TenantId ||
                string.IsNullOrWhiteSpace(record.IssuerName) || record.IssuerName.Length > 300 ||
                record.StudentId != issue.StudentId || record.TranscriptReference != issue.PublicId ||
                record.TranscriptNumber != issue.TranscriptNumber || record.IssueDate != issue.IssueDate ||
                record.IssuedByUserId != issue.IssuedByUserId || record.AcademicRecord == null ||
                record.AcademicRecord.StudentReference == Guid.Empty ||
                record.AcademicRecord.Courses == null || record.AcademicRecord.Courses.Count is < 1 or > 2500 ||
                record.AcademicRecord.LegacyPublishedResults != 0 ||
                record.AcademicRecord.Courses.Any(c => c.PublicationId <= 0 ||
                    c.PublicationVersionNo <= 0 || c.CreditHours < 0 || c.FullMarks <= 0 ||
                    c.ObtainedMarks < 0 || c.ObtainedMarks > c.FullMarks || c.IsWithheld))
                return false;
            snapshot = record;
            return true;
        }
        catch (CryptographicException) { return false; }
        catch (JsonException) { return false; }
    }
}
