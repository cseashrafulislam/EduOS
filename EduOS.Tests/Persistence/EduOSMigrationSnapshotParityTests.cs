using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EduOS.Tests.Persistence;

public sealed class EduOSMigrationSnapshotParityTests
{
    [Fact]
    public void Committed_model_snapshot_matches_the_current_EduOS_model()
    {
        // Metadata-only check; this must not connect to or mutate any database.
        var options = new DbContextOptionsBuilder<EduOSDbContext>()
            .UseSqlServer("Server=localhost;Database=EduOS_ModelContractOnly;Integrated Security=True;TrustServerCertificate=True")
            .Options;

        using var context = new EduOSDbContext(options);
        Assert.False(context.Database.HasPendingModelChanges(),
            "The current EF model differs from the committed migrations snapshot. " +
            "Review the scaffolded diff and commit a safe migration or correct the snapshot.");
    }
}
