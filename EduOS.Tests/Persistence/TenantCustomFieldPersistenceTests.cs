using System.Reflection;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Library;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class TenantCustomFieldPersistenceTests
{
    [Fact]
    public void Active_tenant_field_value_has_unique_definition_entity_index()
    {
        using var db = Db();
        var entity = db.Model.FindEntityType(typeof(CustomFieldValue))!;
        var index = entity.GetIndexes().Single(x =>
            x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "CustomFieldDefinitionId", "EntityId" }));
        Assert.True(index.IsUnique);
        Assert.Equal("[IsDeleted] = 0", index.GetFilter());
    }

    [Fact]
    public void Audit_suppresses_raw_custom_field_values()
    {
        var method = typeof(EduOSDbContext).GetMethod("SuppressAuditPayload", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, new object[] { new CustomFieldValue() })!);
    }

    [Fact]
    public void Audit_suppresses_raw_omr_answer_history()
    {
        var method = typeof(EduOSDbContext).GetMethod("SuppressAuditPayload", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, new object[]
        { new OmrSheetEvent { BeforeAnswers = "ABCD", AfterAnswers = "ABCE" } })!);
    }

    [Fact]
    public void Audit_suppresses_private_reader_positions_and_idempotency_references()
    {
        var method = typeof(EduOSDbContext).GetMethod("SuppressAuditPayload", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        Assert.True((bool)method!.Invoke(null, new object[]
        { new StudyBookmark { LastReadPage = 55, LastProgressCommandId = Guid.NewGuid() } })!);
    }

    private static EduOSDbContext Db()
    {
        var http = new DefaultHttpContext(); http.Items["TenantId"] = 1L;
        return new EduOSDbContext(new DbContextOptionsBuilder<EduOSDbContext>()
            .UseInMemoryDatabase("tenant-custom-fields-" + Guid.NewGuid().ToString("N")).Options,
            new HttpContextAccessor { HttpContext = http });
    }
}
