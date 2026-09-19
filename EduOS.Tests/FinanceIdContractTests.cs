using EduOS.Core.Entities.Finance;
using Xunit;

namespace EduOS.Tests;

public sealed class FinanceIdContractTests
{
    [Theory]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.Id), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.TenantId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.AcademicYearId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.ClassId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.FeeHeadId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.Id), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.TenantId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.StudentId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.AcademicYearId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.ClassId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.SectionId), typeof(long))]
    public void Mapped_finance_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
