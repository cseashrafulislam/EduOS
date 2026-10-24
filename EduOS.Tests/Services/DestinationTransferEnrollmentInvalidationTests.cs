using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class DestinationTransferEnrollmentInvalidationTests
{
    [Fact]
    public async Task Enrollment_transition_invalidates_only_its_destination_confirmation()
    {
        await using var db = NewContext();
        using var tenant = db.BeginSystemTenantScope(77);
        var now = DateTime.UtcNow;
        db.StudentEnrollments.AddRange(
            Enrollment(101, 1001),
            Enrollment(102, 1002));
        db.TransferRequests.AddRange(
            Transfer(201, 101, 1001, now),
            Transfer(202, 102, 1002, now));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var changed = await db.StudentEnrollments.SingleAsync(x => x.Id == 101);
        changed.IsCurrent = false;
        changed.State = EnrollmentState.Transferred;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var invalidated = await db.TransferRequests.SingleAsync(x => x.Id == 201);
        invalidated.DestinationEnrollmentConfirmedAt.Should().BeNull();
        invalidated.DestinationEnrollmentConfirmedByUserId.Should().BeNull();
        invalidated.DestinationEnrollmentVersionAtConfirmation.Should().BeNull();

        var unrelated = await db.TransferRequests.SingleAsync(x => x.Id == 202);
        unrelated.DestinationEnrollmentConfirmedAt.Should().Be(now);
        unrelated.DestinationEnrollmentConfirmedByUserId.Should().Be(900);
        unrelated.DestinationEnrollmentVersionAtConfirmation.Should().Be("enrollment-version");
    }

    [Fact]
    public async Task Enrollment_metadata_update_also_invalidates_attestation_when_version_changes()
    {
        await using var db = NewContext();
        using var tenant = db.BeginSystemTenantScope(77);
        db.StudentEnrollments.Add(Enrollment(301, 3001));
        db.TransferRequests.Add(Transfer(401, 301, 3001, DateTime.UtcNow));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var enrollment = await db.StudentEnrollments.SingleAsync(x => x.Id == 301);
        enrollment.Remarks = "Corrected by authorized enrollment workflow";
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        (await db.TransferRequests.SingleAsync(x => x.Id == 401))
            .DestinationEnrollmentConfirmedAt.Should().BeNull();
    }

    private static EduOSDbContext NewContext() => new(
        new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("destination-transfer-invalidation-" + Guid.NewGuid().ToString("N"))
            .Options);

    private static StudentEnrollment Enrollment(long id, long studentId) => new()
    {
        Id = id, TenantId = 77, StudentId = studentId,
        ClientRequestId = Guid.NewGuid(), RollNo = id.ToString(),
        EnrollmentDate = new DateOnly(2026, 10, 1),
        State = EnrollmentState.Active, IsCurrent = true
    };

    private static TransferRequest Transfer(long id, long enrollmentId, long studentId, DateTime confirmedAt) => new()
    {
        Id = id, SourceTenantId = 88, SourceStudentId = id + 10000,
        SourceEnrollmentId = id + 20000, DestinationTenantId = 77,
        DestinationStudentId = studentId, DestinationEnrollmentId = enrollmentId,
        ClientRequestId = Guid.NewGuid(), RequestedByUserId = 900,
        State = TransferRequestState.DestinationAccepted,
        DestinationEnrollmentConfirmedAt = confirmedAt,
        DestinationEnrollmentConfirmedByUserId = 900,
        DestinationEnrollmentVersionAtConfirmation = "enrollment-version"
    };
}
