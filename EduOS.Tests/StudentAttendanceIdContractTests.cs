using EduOS.Core.DTOs.Attendance;
using Xunit;

namespace EduOS.Tests;

public sealed class StudentAttendanceIdContractTests
{
    [Fact]
    public void Attendance_writes_use_enrollment_reference_not_internal_student_id()
    {
        Assert.Equal(typeof(Guid), typeof(SaveStudentAttendanceRequestDto)
            .GetProperty(nameof(SaveStudentAttendanceRequestDto.StudentEnrollmentReference))!.PropertyType);
        Assert.Null(typeof(SaveStudentAttendanceRequestDto).GetProperty("StudentId"));
    }

    [Fact]
    public void Attendance_reads_expose_public_student_and_enrollment_references()
    {
        Assert.Equal(typeof(Guid), typeof(StudentAttendanceDto)
            .GetProperty(nameof(StudentAttendanceDto.StudentReference))!.PropertyType);
        Assert.Equal(typeof(Guid), typeof(StudentAttendanceDto)
            .GetProperty(nameof(StudentAttendanceDto.StudentEnrollmentReference))!.PropertyType);
        Assert.Null(typeof(StudentAttendanceDto).GetProperty("StudentId"));
    }
}
