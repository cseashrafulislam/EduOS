using EduOS.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Auth
{
    public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
    {
        public void Configure(EntityTypeBuilder<ApplicationUser> builder)
        {
            // Identity already names this 'AspNetUsers'. Keep that for compatibility.

            builder.Property(u => u.FullName).IsRequired().HasMaxLength(150);
            builder.Property(u => u.FirstName).HasMaxLength(75);
            builder.Property(u => u.LastName).HasMaxLength(75);
            builder.Property(u => u.PhotoUrl).HasMaxLength(500);
            builder.Property(u => u.Address).HasMaxLength(500);
            builder.Property(u => u.Gender).HasMaxLength(20);
            builder.Property(u => u.UserType).HasMaxLength(50);
            builder.Property(u => u.LastLoginIp).HasMaxLength(50);
            builder.Property(u => u.RefreshToken).HasMaxLength(500);

            // Indexes
            builder.HasIndex(u => u.TenantId);
            builder.HasIndex(u => u.UserType);
            builder.HasIndex(u => new { u.TenantId, u.IsActive });

            // Tenant relationship
            builder.HasOne(u => u.Tenant)
                .WithMany()
                .HasForeignKey(u => u.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            // Soft delete query filter
            builder.HasQueryFilter(u => !u.IsDeleted);
        }
    }

    public class ApplicationRoleConfiguration : IEntityTypeConfiguration<ApplicationRole>
    {
        public void Configure(EntityTypeBuilder<ApplicationRole> builder)
        {
            builder.Property(r => r.Description).HasMaxLength(500);

            builder.HasIndex(r => r.TenantId);
            builder.HasIndex(r => new { r.TenantId, r.Name });

            builder.HasQueryFilter(r => !r.IsDeleted);
        }
    }
}
