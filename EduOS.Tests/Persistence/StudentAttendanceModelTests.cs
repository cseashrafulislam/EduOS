using EduOS.Core.Entities.Attendance;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class StudentAttendanceModelTests
{
    [Fact]
    public void Attendance_model_enforces_daily_uniqueness_and_valid_values()
    {
        using var context = new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>().UseInMemoryDatabase($"attendance-model-{Guid.NewGuid():N}").Options);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StudentAttendance))!;

        entity.FindProperty(nameof(StudentAttendance.Date))!.FindAnnotation("Relational:ColumnType")!.Value.Should().Be("date");
        entity.FindProperty(nameof(StudentAttendance.Status))!.GetMaxLength().Should().Be(20);
        entity.GetIndexes().Single(x => x.GetDatabaseName() == "UX_StudentAttendances_Tenant_Student_Date").IsUnique.Should().BeTrue();
        entity.GetIndexes().Should().ContainSingle(x => x.GetDatabaseName() == "IX_StudentAttendances_Tenant_Roster_Date");
        entity.GetCheckConstraints().Should().Contain(x => x.Name == "CK_StudentAttendances_Status");
        entity.GetCheckConstraints().Should().Contain(x => x.Name == "CK_StudentAttendances_TimeRange");
        entity.GetForeignKeys().Should().OnlyContain(x => x.DeleteBehavior == DeleteBehavior.Restrict);
    }
}
