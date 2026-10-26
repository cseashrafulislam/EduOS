using Xunit;

namespace EduOS.Tests.Services;

public sealed class TransportNormalizedIndexMigrationContractTests
{
    [Theory]
    [InlineData("EduOS.Persistence", "Context", "EduOSDbContext.cs")]
    [InlineData("EduOS.Persistence", "Migrations", "20261026084645_TransportNormalizedIdentityUniqueness.cs")]
    [InlineData("EduOS.Persistence", "Migrations", "20261026084645_TransportNormalizedIdentityUniqueness.Designer.cs")]
    [InlineData("EduOS.Persistence", "Migrations", "20261026092535_ReservationCommandReceipts.Designer.cs")]
    [InlineData("EduOS.Persistence", "Migrations", "EduOSDbContextModelSnapshot.cs")]
    public void Filtered_driver_identity_index_uses_source_column_in_filter(
        string project, string directory, string file)
    {
        var source = Read(project, directory, file);
        Assert.Contains("[LicenseNumber] IS NOT NULL AND [IsDeleted] = 0", source);
        Assert.DoesNotContain("[NormalizedLicenseNumber] IS NOT NULL AND [IsDeleted] = 0", source);
    }

    private static string Read(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory != null; directory = directory.Parent)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
