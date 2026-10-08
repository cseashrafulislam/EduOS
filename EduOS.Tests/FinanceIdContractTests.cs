using EduOS.Core.Entities.Finance;
using Xunit;

namespace EduOS.Tests;

public sealed class FinanceIdContractTests
{
    [Theory]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.Id), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.TenantId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.CampusId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.AcademicYearId), typeof(long))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.AcademicProgramId), typeof(long?))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.AcademicLevelId), typeof(long?))]
    [InlineData(typeof(FeeStructure), nameof(FeeStructure.AcademicBatchId), typeof(long?))]
    [InlineData(typeof(FeeStructureLine), nameof(FeeStructureLine.FeeStructureId), typeof(long))]
    [InlineData(typeof(FeeStructureLine), nameof(FeeStructureLine.FeeHeadId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.Id), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.TenantId), typeof(long))]
    [InlineData(typeof(StudentInvoice), nameof(StudentInvoice.StudentEnrollmentId), typeof(long))]
    [InlineData(typeof(StudentInvoiceLine), nameof(StudentInvoiceLine.StudentInvoiceId), typeof(long))]
    [InlineData(typeof(StudentPayment), nameof(StudentPayment.StudentId), typeof(long))]
    [InlineData(typeof(PaymentAllocation), nameof(PaymentAllocation.StudentInvoiceId), typeof(long))]
    public void Canonical_finance_identifiers_remain_long(Type type, string propertyName, Type expectedType)
    {
        var property = type.GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Equal(expectedType, property!.PropertyType);
    }
}
