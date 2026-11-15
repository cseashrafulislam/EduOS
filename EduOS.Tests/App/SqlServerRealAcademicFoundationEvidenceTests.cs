using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerRealAcademicFoundationEvidenceTests
{
    [Fact]
    public async Task Browser_created_programme_class_subject_and_independent_section_are_tenant_scoped()
    {
        var connection = Environment.GetEnvironmentVariable("EDUOS_SQL_REAL_QA_CONNECTION");
        var programName = Environment.GetEnvironmentVariable("QA_PROGRAM_NAME");
        var levelName = Environment.GetEnvironmentVariable("QA_LEVEL_NAME");
        var subjectName = Environment.GetEnvironmentVariable("QA_SUBJECT_NAME");
        var sectionName = Environment.GetEnvironmentVariable("QA_SECTION_NAME");

        // This test must not run against developer or production databases.
        // Normal CI runs without these variables; partial configuration is fatal.
        if (new[] { connection, programName, levelName, subjectName, sectionName }
            .All(string.IsNullOrWhiteSpace)) return;

        Assert.False(string.IsNullOrWhiteSpace(connection));
        Assert.StartsWith("QA Programme ", programName);
        Assert.StartsWith("QA Class One ", levelName);
        Assert.StartsWith("QA Subject ", subjectName);
        Assert.StartsWith("QA Section A ", sectionName);
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer(connection).Options;
        await using var db = new EduOSDbContext(options);
        Assert.Contains(db.Database.GetDbConnection().Database,
            new[] { "EduOS_Demo_AutoQa", "EduOS_Demo_Codespaces" });
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Code == "EDUOS-DEMO" && !x.IsDeleted);
        var campus = await db.Campuses.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Code == "DEMO-C01" && !x.IsDeleted);
        var program = await db.AcademicPrograms.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == programName && !x.IsDeleted);
        var level = await db.AcademicLevels.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == levelName && !x.IsDeleted);
        var subject = await db.Subjects.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == subjectName && !x.IsDeleted);
        var section = await db.AcademicSections.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == sectionName && !x.IsDeleted);

        Assert.Equal(program.Id, level.AcademicProgramId);
        Assert.Equal(level.Id, section.AcademicLevelId);
        Assert.Equal(campus.Id, section.CampusId);
        Assert.Equal(35, section.Capacity);
        Assert.Equal(1, level.LevelNo);
        Assert.Equal(1.50m, subject.DefaultCreditHours);
        Assert.True(program.Id > 0 && level.Id > 0 && section.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(program.Code));
        Assert.False(string.IsNullOrWhiteSpace(level.Code));
        Assert.False(string.IsNullOrWhiteSpace(subject.Code));
        Assert.False(string.IsNullOrWhiteSpace(section.Code));

        // A Section belongs to a Class + Campus, never to a required Batch.
        Assert.False(await db.AcademicBatches.IgnoreQueryFilters().AsNoTracking().AnyAsync(
            x => x.TenantId == tenant.Id && x.AcademicLevelId == level.Id && !x.IsDeleted));
    }
}
