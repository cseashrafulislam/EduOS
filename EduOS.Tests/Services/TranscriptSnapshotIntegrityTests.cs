using EduOS.Core.DTOs.Assessment;
using EduOS.Core.Entities.Assessment;
using EduOS.Service.Services.AssessmentAdministration;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class TranscriptSnapshotIntegrityTests
{
    private static readonly DateOnly IssueDate = new(2026, 10, 29);

    [Fact]
    public void Authenticated_issue_time_evidence_is_readable_but_metadata_swaps_or_corruption_are_rejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var protector = provider.CreateProtector(TranscriptSnapshotIntegrity.Purpose);
        var issue = new TranscriptIssue
        {
            TenantId = 17, StudentId = 23, PublicId = Guid.NewGuid(),
            TranscriptNumber = "TRN-SECURE", IssueDate = IssueDate,
            IssuedByUserId = 8
        };
        var record = new TranscriptSnapshotDocumentDto
        {
            TenantId = issue.TenantId, StudentId = issue.StudentId,
            TranscriptReference = issue.PublicId, TranscriptNumber = issue.TranscriptNumber,
            IssueDate = issue.IssueDate, IssuedByUserId = issue.IssuedByUserId,
            IssuerName = "Verified Institute", IssuedAtUtc = new DateTime(2026, 10, 29, 8, 0, 0, DateTimeKind.Utc),
            AcademicRecord = new TranscriptAcademicRecordDto
            {
                StudentReference = Guid.NewGuid(), StudentCode = "ST-01", StudentName = "Test Student",
                Courses = [new TranscriptAcademicCourseDto
                {
                    PublicationId = 101, PublicationVersionNo = 1,
                    SubjectCode = "MATH", SubjectName = "Mathematics", FullMarks = 100,
                    ObtainedMarks = 76, CreditHours = 3, GradePoint = 3.5m
                }]
            }
        };
        issue.ProtectedAcademicRecord = protector.Protect(JsonSerializer.Serialize(record));
        TranscriptSnapshotIntegrity.TryRead(protector, issue, out var restored).Should().BeTrue();
        restored!.AcademicRecord.Courses.Single().SubjectName.Should().Be("Mathematics");

        issue.TenantId = 18;
        TranscriptSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        issue.TenantId = 17;
        issue.TranscriptNumber = "TRN-SWAPPED";
        TranscriptSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        issue.TranscriptNumber = "TRN-SECURE";
        issue.ProtectedAcademicRecord += "broken";
        TranscriptSnapshotIntegrity.TryRead(protector, issue, out _).Should().BeFalse();
        TranscriptSnapshotIntegrity.TryRead(null, issue, out _).Should().BeFalse();
    }

    [Fact]
    public void Fresh_issue_does_not_accept_foreign_key_ring_or_legacy_metadata_only_record()
    {
        var first = new EphemeralDataProtectionProvider().CreateProtector(TranscriptSnapshotIntegrity.Purpose);
        var second = new EphemeralDataProtectionProvider().CreateProtector(TranscriptSnapshotIntegrity.Purpose);
        var issue = new TranscriptIssue { ProtectedAcademicRecord = first.Protect("{}") };
        TranscriptSnapshotIntegrity.TryRead(second, issue, out _).Should().BeFalse();
        issue.ProtectedAcademicRecord = null;
        TranscriptSnapshotIntegrity.TryRead(first, issue, out _).Should().BeFalse();
    }
}
