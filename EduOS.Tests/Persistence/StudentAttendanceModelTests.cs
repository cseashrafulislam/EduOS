using EduOS.Core.Entities.Attendance;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class StudentAttendanceModelTests
{
    [Fact]
    public void Attendance_model_enforces_session_enrollment_uniqueness_and_concurrency()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase($"attendance-model-{Guid.NewGuid():N}").Options);
        var entity = context.Model.FindEntityType(typeof(StudentAttendance))!;

        entity.GetQueryFilter().Should().NotBeNull();
        entity.FindProperty(nameof(StudentAttendance.AttendanceSessionId))!.ClrType.Should().Be(typeof(long));
        entity.FindProperty(nameof(StudentAttendance.StudentEnrollmentId))!.ClrType.Should().Be(typeof(long));
        entity.FindProperty(nameof(StudentAttendance.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        entity.FindProperty(nameof(StudentAttendance.State))!.ClrType.IsEnum.Should().BeTrue();
        entity.GetIndexes().Should().Contain(x => x.IsUnique &&
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(StudentAttendance.TenantId),
                nameof(StudentAttendance.AttendanceSessionId), nameof(StudentAttendance.StudentEnrollmentId) }));
    }
}
