using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Tenants
{
    public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
    {
        public void Configure(EntityTypeBuilder<Tenant> builder)
        {
            builder.ToTable("Tenants");
            builder.HasKey(t => t.Id);

            // Identification
            builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
            builder.Property(t => t.Code).IsRequired().HasMaxLength(50);
            builder.Property(t => t.Subdomain).HasMaxLength(100);
            builder.Property(t => t.CustomDomain).HasMaxLength(200);
            builder.Property(t => t.InstitutionType).HasMaxLength(50);

            // Contact
            builder.Property(t => t.Email).IsRequired().HasMaxLength(150);
            builder.Property(t => t.Phone).HasMaxLength(20);
            builder.Property(t => t.Website).HasMaxLength(200);
            builder.Property(t => t.Address).HasMaxLength(500);
            builder.Property(t => t.City).HasMaxLength(100);
            builder.Property(t => t.State).HasMaxLength(100);
            builder.Property(t => t.Country).HasMaxLength(100);
            builder.Property(t => t.PostalCode).HasMaxLength(20);

            // Owner
            builder.Property(t => t.OwnerName).IsRequired().HasMaxLength(150);
            builder.Property(t => t.OwnerPhone).HasMaxLength(20);
            builder.Property(t => t.OwnerEmail).HasMaxLength(150);
            builder.Property(t => t.OwnerDesignation).HasMaxLength(100);

            // Branding
            builder.Property(t => t.LogoUrl).HasMaxLength(500);
            builder.Property(t => t.FaviconUrl).HasMaxLength(500);
            builder.Property(t => t.PrimaryColor).HasMaxLength(20);
            builder.Property(t => t.SecondaryColor).HasMaxLength(20);
            builder.Property(t => t.AccentColor).HasMaxLength(20);

            // Localization
            builder.Property(t => t.Currency).HasMaxLength(10);
            builder.Property(t => t.CurrencySymbol).HasMaxLength(10);
            builder.Property(t => t.TimeZone).HasMaxLength(50);
            builder.Property(t => t.Language).HasMaxLength(10);
            builder.Property(t => t.DateFormat).HasMaxLength(20);

            // Status
            builder.Property(t => t.Status).HasConversion<int>();
            builder.Property(t => t.OnboardingStep).HasConversion<int>();
            builder.Property(t => t.SuspensionReason).HasMaxLength(500);

            // Indexes
            builder.HasIndex(t => t.Code).IsUnique().HasFilter("[IsDeleted] = 0");
            builder.HasIndex(t => t.Subdomain).IsUnique().HasFilter("[Subdomain] IS NOT NULL AND [IsDeleted] = 0");
            builder.HasIndex(t => t.CustomDomain).IsUnique().HasFilter("[CustomDomain] IS NOT NULL AND [IsDeleted] = 0");
            builder.HasIndex(t => t.Email);
            builder.HasIndex(t => t.Status);
            builder.HasIndex(t => t.InstitutionTypeDefinitionId);

            // Relationships
            builder.HasOne(t => t.InstitutionTypeDefinition)
                .WithMany(i => i.Tenants)
                .HasForeignKey(t => t.InstitutionTypeDefinitionId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(t => t.Settings)
                .WithOne(s => s.Tenant)
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(t => t.Subscriptions)
                .WithOne(s => s.Tenant)
                .HasForeignKey(s => s.TenantId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
