using EduOS.Persistence.Context;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.App;

[Collection("SqlServerIntegration")]
public sealed class SqlServerRealDataEntryEvidenceTests
{
    [Fact]
    public async Task Browser_created_year_and_term_are_committed_once_in_demo_tenant()
    {
        var conn = Environment.GetEnvironmentVariable("EDUOS_SQL_REAL_QA_CONNECTION");
        var yearName = Environment.GetEnvironmentVariable("QA_YEAR_NAME");
        var termName = Environment.GetEnvironmentVariable("QA_TERM_NAME");
        // This expensive SQL/UI evidence is opt-in and only runs with the
        // disposable real-browser QA job. Fail closed on partial configuration.
        if (string.IsNullOrWhiteSpace(conn) && string.IsNullOrWhiteSpace(yearName) &&
            string.IsNullOrWhiteSpace(termName)) return;
        Assert.False(string.IsNullOrWhiteSpace(conn));
        Assert.StartsWith("QA Academic Year ", yearName);
        Assert.StartsWith("QA First Term ", termName);
        var target = new SqlConnectionStringBuilder(conn);
        Assert.Equal("127.0.0.1,14333", target.DataSource);
        Assert.Contains(target.InitialCatalog, new[] { "EduOS_Demo_AutoQa", "EduOS_Demo_Codespaces" });
        var options = new DbContextOptionsBuilder<EduOSDbContext>().UseSqlServer(conn).Options;
        await using var db = new EduOSDbContext(options);
        Assert.Equal(target.InitialCatalog, db.Database.GetDbConnection().Database);
        var tenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.Code == "EDUOS-DEMO" && !x.IsDeleted);
        var year = await db.AcademicYears.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == yearName && !x.IsDeleted);
        var term = await db.AcademicTerms.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.TenantId == tenant.Id && x.Name == termName && !x.IsDeleted);
        Assert.True(year.Id > 0 && term.Id > 0);
        Assert.False(string.IsNullOrWhiteSpace(year.Code));
        Assert.False(string.IsNullOrWhiteSpace(term.Code));
        Assert.Equal(year.Id,term.AcademicYearId);
        Assert.Equal(new DateOnly(2027,1,1),year.StartDate);
        Assert.Equal(new DateOnly(2027,12,31),year.EndDate);
        Assert.Equal(new DateOnly(2027,2,1),term.StartDate);
        Assert.Equal(new DateOnly(2027,6,30),term.EndDate);
    }
}
