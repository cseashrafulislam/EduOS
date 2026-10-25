using EduOS.Core.DTOs.Attendance;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Service.Services.Attendance;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class StudentLeaveServiceTests
{
    private static readonly DateOnly From = new(2026, 9, 14);
    private static readonly DateOnly To = new(2026, 9, 16);

    [Fact]
    public async Task Student_can_submit_own_leave_replay_safely_but_not_another_students_leave()
    {
        var options = Options();
        var (mine, foreign) = await Seed(options);
        await using var db = Context(options, 101);
        var service = Service(db, new Actor(101, 11, "Student"));
        var request = new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Medical appointment"
        };
        var first = await service.ApplyAsync(request);
        var replay = await service.ApplyAsync(request);
        first.Success.Should().BeTrue(first.Message);
        replay.Success.Should().BeTrue(replay.Message);
        first.Data!.Id.Should().Be(replay.Data!.Id);
        (await db.StudentLeaveApplications.CountAsync()).Should().Be(1);
        request.Reason = "Changed request";
        (await service.ApplyAsync(request)).StatusCode.Should().Be(409);
        request.ClientRequestId = Guid.NewGuid();
        request.StudentEnrollmentReference = foreign;
        (await service.ApplyAsync(request)).StatusCode.Should().Be(404);
        (await db.StudentLeaveApplications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Overlap_and_cross_tenant_reads_are_blocked_and_paging_is_bounded()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using (var own = Context(options, 101))
        {
            var manager = Service(own, new Actor(101, 7, "TenantAdmin"));
            var first = await manager.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
                FromDate = From, ToDate = To, Reason = "Family leave"
            });
            first.Success.Should().BeTrue(first.Message);
            var duplicate = await manager.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
            {
                ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
                FromDate = new DateOnly(2026, 9, 15), ToDate = new DateOnly(2026, 9, 17),
                Reason = "Overlapping leave"
            });
            duplicate.StatusCode.Should().Be(409);
            var page = await manager.GetApplicationsAsync(mine, LeaveState.Submitted, 1, 20);
            page.Success.Should().BeTrue();
            page.Data!.TotalCount.Should().Be(1);
            (await manager.GetApplicationsAsync(null, null, 0, 20)).StatusCode.Should().Be(400);
            (await manager.GetApplicationsAsync(null, null, 1, 101)).StatusCode.Should().Be(400);
        }
        await using var other = Context(options, 202);
        var tenant202 = Service(other, new Actor(202, 14, "TenantAdmin"));
        (await tenant202.GetApplicationsAsync(mine, null, 1, 20)).StatusCode.Should().Be(404);
        (await tenant202.GetApplicationsAsync(null, null, 1, 20)).Data!.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task Only_manager_can_approve_submitted_leave_with_matching_version()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using var db = Context(options, 101);
        var applicant = Service(db, new Actor(101, 11, "Student"));
        var created = await applicant.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Family travel"
        });
        created.Success.Should().BeTrue();
        var row = await db.StudentLeaveApplications.SingleAsync();
        row.RowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        await db.SaveChangesAsync();
        var manager = Service(db, new Actor(101, 7, "TenantAdmin"));
        var decision = new ReviewStudentLeaveRequestDto
        {
            State = LeaveState.Approved, ReviewNote = "Approved",
            RowVersion = Convert.ToBase64String(row.RowVersion)
        };
        (await applicant.ReviewAsync(row.Id, decision)).StatusCode.Should().Be(403);
        decision.RowVersion = Convert.ToBase64String([7, 7, 7, 7, 7, 7, 7, 7]);
        (await manager.ReviewAsync(row.Id, decision)).StatusCode.Should().Be(409);
        decision.RowVersion = Convert.ToBase64String(row.RowVersion);
        var approved = await manager.ReviewAsync(row.Id, decision);
        approved.Success.Should().BeTrue(approved.Message);
        approved.Data!.State.Should().Be(LeaveState.Approved);
        (await manager.ReviewAsync(row.Id, decision)).StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task Student_can_cancel_submitted_leave_idempotently_but_other_students_cannot()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using var db = Context(options, 101);
        var owner = Service(db, new Actor(101, 11, "Student"));
        var created = await owner.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Medical leave"
        });
        created.Success.Should().BeTrue();
        var row = await db.StudentLeaveApplications.SingleAsync();
        row.RowVersion = [1, 2, 3, 4];
        await db.SaveChangesAsync();
        var request = new CancelStudentLeaveRequestDto
        {
            Reason = "Appointment moved", RowVersion = Convert.ToBase64String(row.RowVersion)
        };
        var stranger = Service(db, new Actor(101, 12, "Student"));
        (await stranger.CancelAsync(row.Id, request)).StatusCode.Should().Be(404);
        var cancelled = await owner.CancelAsync(row.Id, request);
        cancelled.Success.Should().BeTrue(cancelled.Message);
        cancelled.Data!.State.Should().Be(LeaveState.Cancelled);
        (await owner.CancelAsync(row.Id, request)).Success.Should().BeTrue();
        request.Reason = "Another reason";
        (await owner.CancelAsync(row.Id, request)).StatusCode.Should().Be(409);
        var replacement = await owner.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Replacement"
        });
        replacement.Success.Should().BeTrue(replacement.Message);
    }

    [Fact]
    public async Task Cancellation_rejects_stale_version_approved_leave_and_foreign_tenant()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using var db = Context(options, 101);
        var manager = Service(db, new Actor(101, 7, "TenantAdmin"));
        var created = await manager.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Family event"
        });
        created.Success.Should().BeTrue();
        var row = await db.StudentLeaveApplications.SingleAsync();
        row.RowVersion = [3, 3, 3, 3];
        await db.SaveChangesAsync();
        var request = new CancelStudentLeaveRequestDto
        {
            Reason = "No longer needed", RowVersion = Convert.ToBase64String([9, 9, 9, 9])
        };
        (await manager.CancelAsync(row.Id, request)).StatusCode.Should().Be(409);
        var approved = await manager.ReviewAsync(row.Id, new ReviewStudentLeaveRequestDto
        {
            State = LeaveState.Approved, RowVersion = Convert.ToBase64String(row.RowVersion)
        });
        approved.Success.Should().BeTrue(approved.Message);
        request.RowVersion = Convert.ToBase64String(row.RowVersion);
        (await manager.CancelAsync(row.Id, request)).StatusCode.Should().Be(409);
        // HttpContextAccessor shares ambient state: switch tenants only after
        // the original tenant's review and cancellation checks have completed.
        await using var otherDb = Context(options, 202);
        (await Service(otherDb, new Actor(202, 14, "TenantAdmin"))
            .CancelAsync(row.Id, request)).StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Verified_private_file_is_attached_once_and_idempotent_leave_retry_preserves_it()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using var db = Context(options, 101);
        var asset = new FileAsset
        {
            TenantId = 101, PublicId = Guid.NewGuid(), UploadedByUserId = 11,
            OriginalFileName = "evidence.pdf", ContentType = "application/pdf",
            StorageProvider = "LocalPrivate", StorageKey = "tenant-101/documents/evidence.pdf",
            SizeBytes = 4, Sha256 = new string('A', 64), IsVerifiedSafe = true,
            MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
        };
        db.FileAssets.Add(asset);
        await db.SaveChangesAsync();
        var service = Service(db, new Actor(101, 11, "Student"));
        var request = new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Medical evidence",
            AttachmentFileId = asset.Id
        };
        var first = await service.ApplyAsync(request);
        first.Success.Should().BeTrue(first.Message);
        first.Data!.AttachmentFileId.Should().Be(asset.Id);
        (await service.ApplyAsync(request)).Data!.Id.Should().Be(first.Data.Id);
        var second = await service.ApplyAsync(new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = new DateOnly(2026, 9, 20), ToDate = new DateOnly(2026, 9, 21),
            Reason = "Another request", AttachmentFileId = asset.Id
        });
        second.StatusCode.Should().Be(409);
        (await db.StudentLeaveApplications.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Unscanned_foreign_uploader_and_cross_tenant_file_references_are_denied()
    {
        var options = Options();
        var (mine, _) = await Seed(options);
        await using var db = Context(options, 101);
        var file = new FileAsset
        {
            TenantId = 101, UploadedByUserId = 12, OriginalFileName = "note.pdf",
            ContentType = "application/pdf", StorageProvider = "LocalPrivate",
            StorageKey = "tenant-101/documents/note.pdf", SizeBytes = 3,
            Sha256 = new string('B', 64), IsVerifiedSafe = true,
            MalwareScanStateCode = "Clean", Visibility = FileVisibility.Private
        };
        db.FileAssets.Add(file);
        await db.SaveChangesAsync();
        var service = Service(db, new Actor(101, 11, "Student"));
        var request = new CreateStudentLeaveApplicationRequestDto
        {
            ClientRequestId = Guid.NewGuid(), StudentEnrollmentReference = mine,
            FromDate = From, ToDate = To, Reason = "Leave", AttachmentFileId = file.Id
        };
        (await service.ApplyAsync(request)).StatusCode.Should().Be(404);
        file.UploadedByUserId = 11; file.IsVerifiedSafe = false;
        await db.SaveChangesAsync();
        (await service.ApplyAsync(request)).StatusCode.Should().Be(404);
        file.IsVerifiedSafe = true; file.TenantId = 202;
        await db.SaveChangesAsync();
        (await service.ApplyAsync(request)).StatusCode.Should().Be(404);
        (await db.StudentLeaveApplications.CountAsync()).Should().Be(0);
    }

    private static StudentLeaveService Service(EduOSDbContext db, ICurrentUserService user) => new(
        new GenericRepository<StudentLeaveApplication>(db),
        new GenericRepository<StudentEnrollment>(db),
        new GenericRepository<Student>(db),
        new GenericRepository<StudentGuardian>(db),
        new GenericRepository<Guardian>(db),
        new GenericRepository<AcademicYear>(db),
        db, user, TimeProvider.System, NullLogger<StudentLeaveService>.Instance,
        db, null!, null!);

    private static async Task<(Guid Mine, Guid Other)> Seed(DbContextOptions<EduOSDbContext> options)
    {
        await using var db = Context(options, 101);
        var year = new AcademicYear { TenantId = 101, Name = "2026", Code = "2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31) };
        var mine = new Student { TenantId = 101, UserId = 11, FullName = "My Student",
            StudentCode = "MY-001", StatusCode = "Active" };
        var other = new Student { TenantId = 101, UserId = 12, FullName = "Other Student",
            StudentCode = "OT-001", StatusCode = "Active" };
        db.AddRange(year, mine, other); await db.SaveChangesAsync();
        var ownEnrollment = new StudentEnrollment { TenantId = 101, StudentId = mine.Id,
            AcademicYearId = year.Id, EnrollmentDate = new DateOnly(2026, 1, 1),
            State = EnrollmentState.Active, IsCurrent = true };
        var otherEnrollment = new StudentEnrollment { TenantId = 101, StudentId = other.Id,
            AcademicYearId = year.Id, EnrollmentDate = new DateOnly(2026, 1, 1),
            State = EnrollmentState.Active, IsCurrent = true };
        db.AddRange(ownEnrollment, otherEnrollment);
        await db.SaveChangesAsync();
        return (ownEnrollment.PublicId, otherEnrollment.PublicId);
    }

    private static DbContextOptions<EduOSDbContext> Options() =>
        new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase(
            "leave-" + Guid.NewGuid().ToString("N")).Options;

    private static EduOSDbContext Context(DbContextOptions<EduOSDbContext> options, long tenant)
    {
        var context = new DefaultHttpContext();
        context.Items["TenantId"] = tenant;
        return new EduOSDbContext(options, new HttpContextAccessor { HttpContext = context });
    }
    private sealed class Actor(long tenantId, long userId, string role) : ICurrentUserService
    {
        public bool IsAuthenticated => true;
        public long TenantId => tenantId;
        public long UserId => userId;
        public bool IsTenantAdmin => role == "TenantAdmin";
        public bool IsSuperAdmin => false;
        public IReadOnlyList<string> Roles => [role];
        public bool IsInRole(string name) => name == role;
        public string? FullName => role;
        public string? Email => "test@example.test";
        public string? IpAddress => "127.0.0.1";
        public string? UserAgent => "Student Leave Tests";
    }
}
