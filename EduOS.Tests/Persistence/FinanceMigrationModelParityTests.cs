using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class FinanceMigrationModelParityTests
{
    [Fact]
    public void Migrations_snapshot_matches_current_SQL_server_model()
    {
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=(local);Database=EduOS_FinanceModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;
        using var db = new EduOSDbContext(options);
        var snapshot = db.GetService<IMigrationsAssembly>().ModelSnapshot;
        Assert.NotNull(snapshot);
        var initialized = db.GetService<IModelRuntimeInitializer>().Initialize(snapshot.Model, designTime:true, validationLogger:null);
        var current = db.GetService<IDesignTimeModel>().Model;
        var operations = db.GetService<IMigrationsModelDiffer>()
            .GetDifferences(initialized.GetRelationalModel(), current.GetRelationalModel());
        var details = string.Join("\n", operations.Select(op => op.GetType().Name + " " + string.Join(" ",
            op.GetType().GetProperties().Where(p => p.PropertyType == typeof(string) || p.PropertyType == typeof(bool))
                .Select(p => p.Name + "=" + p.GetValue(op)))));
        Assert.True(operations.Count == 0, "EF migrations model drift:\n" + details);
    }
}
