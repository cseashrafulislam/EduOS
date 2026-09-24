using EduOS.Core.DTOs.Attendance;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentAttendanceIdContractTests
{
    [Fact]
    public void Attendance_api_uses_public_student_references_only()
    {
        Assert.NotNull(typeof(SaveStudentAttendanceItemDto).GetProperty(nameof(SaveStudentAttendanceItemDto.StudentReference)));
        Assert.Null(typeof(SaveStudentAttendanceItemDto).GetProperty("StudentId"));
        Assert.NotNull(typeof(StudentAttendanceRosterItemDto).GetProperty(nameof(StudentAttendanceRosterItemDto.StudentReference)));
        Assert.Null(typeof(StudentAttendanceRosterItemDto).GetProperty("StudentId"));
    }
}