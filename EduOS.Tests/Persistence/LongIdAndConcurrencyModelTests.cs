using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Transport;
using EduOS.Persistence.Context;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public class LongIdAndConcurrencyModelTests
{
    [Fact]
    public void Normalized_models_use_long_foreign_keys_without_legacy_shadow_id1_properties()
    {
        using var context = CreateContext();
        AssertLongProperties<RoutineEntry>(context, nameof(RoutineEntry.SubjectOfferingId), nameof(RoutineEntry.RoutineTimeSlotId));
        AssertLongProperties<StudentTransport>(context, nameof(StudentTransport.StudentId), nameof(StudentTransport.StudentEnrollmentId), nameof(StudentTransport.VehicleId), nameof(StudentTransport.RouteId));
        AssertLongProperties<EmployeeLeaveApplication>(context, nameof(EmployeeLeaveApplication.EmployeeId), nameof(EmployeeLeaveApplication.LeaveTypeId));
        AssertLongProperties<BookIssue>(context, nameof(BookIssue.BookCopyId), nameof(BookIssue.StudentId), nameof(BookIssue.IssuedByUserId));
        AssertLongProperties<Document>(context, nameof(Document.FileAssetId));
        AssertLongProperties<CustomFieldValue>(context, nameof(CustomFieldValue.CustomFieldDefinitionId), nameof(CustomFieldValue.EntityId));
        AssertLongProperties<ImportLogItem>(context, nameof(ImportLogItem.ImportLogId));
        var entities = new[] { typeof(RoutineEntry), typeof(StudentTransport), typeof(EmployeeLeaveApplication),
            typeof(BookIssue), typeof(Document), typeof(ImportLogItem), typeof(CustomFieldValue), typeof(SubscriptionInvoice) };
        foreach (var entityType in entities)
        {
            var entity = context.Model.FindEntityType(entityType)!;
            entity.GetProperties().Should().NotContain(p => p.IsShadowProperty() && p.Name.EndsWith("Id1", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Subscription_transport_and_library_write_models_keep_optimistic_concurrency_tokens()
    {
        using var context = CreateContext();
        AssertConcurrency<TenantSubscription>(context);
        AssertConcurrency<StudentTransport>(context);
        AssertConcurrency<Book>(context);
        AssertConcurrency<BookIssue>(context);
    }

    [Fact]
    public void Operational_write_models_keep_client_request_idempotency_keys()
    {
        using var context = CreateContext();
        AssertClientRequestId<BookIssue>(context);
        AssertClientRequestId<StudentTransport>(context);
        AssertClientRequestId<EmployeeLeaveApplication>(context);
    }

    private static EduOSDbContext CreateContext() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"long-id-model-{Guid.NewGuid():N}").Options);

    private static void AssertLongProperties<TEntity>(EduOSDbContext context, params string[] propertyNames) where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        foreach (var propertyName in propertyNames)
            entity.FindProperty(propertyName)!.ClrType.Should().Be(typeof(long), $"{typeof(TEntity).Name}.{propertyName} must be a long foreign key");
    }

    private static void AssertConcurrency<TEntity>(EduOSDbContext context) where TEntity : class =>
        context.Model.FindEntityType(typeof(TEntity))!.FindProperty("RowVersion")!.IsConcurrencyToken.Should().BeTrue();

    private static void AssertClientRequestId<TEntity>(EduOSDbContext context) where TEntity : class =>
        context.Model.FindEntityType(typeof(TEntity))!.FindProperty("ClientRequestId").Should().NotBeNull();
}
