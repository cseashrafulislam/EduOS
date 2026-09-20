using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
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

        AssertLongProperties<ClassRoutine>(context, nameof(ClassRoutine.AcademicYearId), nameof(ClassRoutine.ClassId), nameof(ClassRoutine.SectionId), nameof(ClassRoutine.SubjectId), nameof(ClassRoutine.TeacherId));
        AssertLongProperties<StudentTransport>(context, nameof(StudentTransport.StudentId), nameof(StudentTransport.VehicleId), nameof(StudentTransport.RouteId));
        AssertLongProperties<LeaveApplication>(context, nameof(LeaveApplication.UserId), nameof(LeaveApplication.LeaveTypeId));
        AssertLongProperties<TrialAccount>(context, nameof(TrialAccount.TenantId));
        AssertLongProperties<Document>(context, nameof(Document.OwnerId));
        AssertLongProperties<ImportLog>(context, nameof(ImportLog.ImportedBy));
        AssertLongProperties<SurveyQuestion>(context, nameof(SurveyQuestion.SurveyId));
        AssertLongProperties<SurveyResponse>(context, nameof(SurveyResponse.SurveyId), nameof(SurveyResponse.QuestionId));
        AssertLongProperties<AlbumPhoto>(context, nameof(AlbumPhoto.AlbumId));
        AssertLongProperties<CustomFieldValue>(context, nameof(CustomFieldValue.CustomFieldId), nameof(CustomFieldValue.EntityId));

        var trial = context.Model.FindEntityType(typeof(TrialAccount))!;
        trial.FindProperty(nameof(TrialAccount.ConvertedToPlanId))!.ClrType.Should().Be(typeof(long?));

        var academicEvent = context.Model.FindEntityType(typeof(Event))!;
        academicEvent.FindProperty(nameof(Event.OrganizerId))!.ClrType.Should().Be(typeof(long?));

        var complaint = context.Model.FindEntityType(typeof(Complaint))!;
        complaint.FindProperty(nameof(Complaint.SubmittedBy))!.ClrType.Should().Be(typeof(long?));
        complaint.FindProperty(nameof(Complaint.AssignedTo))!.ClrType.Should().Be(typeof(long?));

        var visitor = context.Model.FindEntityType(typeof(Visitor))!;
        visitor.FindProperty(nameof(Visitor.MeetingPersonId))!.ClrType.Should().Be(typeof(long?));

        var surveyResponse = context.Model.FindEntityType(typeof(SurveyResponse))!;
        surveyResponse.FindProperty(nameof(SurveyResponse.RespondentId))!.ClrType.Should().Be(typeof(long?));

        foreach (var entityType in new[] { typeof(ClassRoutine), typeof(StudentTransport), typeof(LeaveApplication), typeof(TrialAccount), typeof(Event), typeof(Document), typeof(ImportLog), typeof(Complaint), typeof(Visitor), typeof(SurveyQuestion), typeof(SurveyResponse), typeof(AlbumPhoto), typeof(CustomFieldValue) })
        {
            var entity = context.Model.FindEntityType(entityType)!;
            entity.GetProperties().Should().NotContain(p => p.PropertyInfo == null && p.FieldInfo == null && p.Name.EndsWith("Id1", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Subscription_transport_and_library_write_models_keep_optimistic_concurrency_tokens()
    {
        using var context = CreateContext();
        var subscription = context.Model.FindEntityType(typeof(TenantSubscription))!;
        var transport = context.Model.FindEntityType(typeof(StudentTransport))!;
        var book = context.Model.FindEntityType(typeof(Book))!;
        var bookIssue = context.Model.FindEntityType(typeof(BookIssue))!;

        subscription.FindProperty(nameof(TenantSubscription.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        transport.FindProperty(nameof(StudentTransport.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        book.FindProperty(nameof(Book.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
        bookIssue.FindProperty(nameof(BookIssue.RowVersion))!.IsConcurrencyToken.Should().BeTrue();
    }

    [Fact]
    public void Operational_write_models_keep_client_request_idempotency_keys()
    {
        using var context = CreateContext();
        AssertClientRequestId<BookIssue>(context);
        AssertClientRequestId<StudentTransport>(context);
    }

    private static EduOSDbContext CreateContext() => new(new DbContextOptionsBuilder<EduOSDbContext>()
        .UseInMemoryDatabase($"long-id-model-{Guid.NewGuid():N}").Options);

    private static void AssertLongProperties<TEntity>(EduOSDbContext context, params string[] propertyNames)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        foreach (var propertyName in propertyNames)
            entity.FindProperty(propertyName)!.ClrType.Should().Be(typeof(long), $"{typeof(TEntity).Name}.{propertyName} is a normalized identifier boundary");
    }

    private static void AssertClientRequestId<TEntity>(EduOSDbContext context)
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))!;
        entity.FindProperty("ClientRequestId").Should().NotBeNull($"{typeof(TEntity).Name} must retain its retry/idempotency correlation key");
    }
}