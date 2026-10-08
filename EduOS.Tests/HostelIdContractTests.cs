using EduOS.Core.Entities.Hostel;
using Xunit;

namespace EduOS.Tests;

public sealed class HostelIdContractTests
{
    [Theory]
    [InlineData(typeof(StudentHostelAllocation), nameof(StudentHostelAllocation.Id), typeof(long))]
    [InlineData(typeof(StudentHostelAllocation), nameof(StudentHostelAllocation.TenantId), typeof(long))]
    [InlineData(typeof(StudentHostelAllocation), nameof(StudentHostelAllocation.StudentId), typeof(long))]
    [InlineData(typeof(StudentHostelAllocation), nameof(StudentHostelAllocation.StudentEnrollmentId), typeof(long))]
    [InlineData(typeof(StudentHostelAllocation), nameof(StudentHostelAllocation.HostelBedId), typeof(long))]
    [InlineData(typeof(HostelBed), nameof(HostelBed.HostelRoomId), typeof(long))]
    public void Hostel_allocation_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
