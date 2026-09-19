using EduOS.Core.Entities.Students;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Students;

public sealed class GuardianConfiguration : IEntityTypeConfiguration<Guardian>
{
    public void Configure(EntityTypeBuilder<Guardian> builder)
    {
        builder.Property(x => x.PublicId).HasDefaultValueSql("NEWSEQUENTIALID()");
        builder.Property(x => x.NameBangla).HasMaxLength(200);
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.IsPrimary }).HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.Phone });
    }
}
