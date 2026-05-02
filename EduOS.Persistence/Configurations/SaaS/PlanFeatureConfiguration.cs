using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.SaaS
{
    public class PlanFeatureConfiguration : IEntityTypeConfiguration<PlanFeature>
    {
        public void Configure(EntityTypeBuilder<PlanFeature> builder)
        {
            builder.ToTable("PlanFeatures");
            builder.HasKey(pf => pf.Id);

            builder.Property(pf => pf.Note).HasMaxLength(500);

            // Composite unique - one feature per plan only once
            builder.HasIndex(pf => new { pf.SubscriptionPlanId, pf.FeatureId })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            builder.HasOne(pf => pf.Feature)
                .WithMany(f => f.PlanFeatures)
                .HasForeignKey(pf => pf.FeatureId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
