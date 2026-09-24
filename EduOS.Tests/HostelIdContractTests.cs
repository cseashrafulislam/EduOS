using EduOS.Core.Entities.Hostel;
using Xunit;

namespace EduOS.Tests;

public sealed class HostelIdContractTests
{
    [Theory]
    [InlineData(typeof(HostelStudent), nameof(HostelStudent.Id), typeof(long))]
    [InlineData(typeof(HostelStudent), nameof(HostelStudent.TenantId), typeof(long))]
    [InlineData(typeof(HostelStudent), nameof(HostelStudent.StudentId), typeof(long))]
    [InlineData(typeof(HostelStudent), nameof(HostelStudent.HostelRoomId), typeof(long))]
    public void Hostel_student_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
