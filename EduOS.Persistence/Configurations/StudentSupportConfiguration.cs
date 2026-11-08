using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations;

public sealed class ScholarshipApplicationConfiguration : IEntityTypeConfiguration<ScholarshipApplication>
{
    public void Configure(EntityTypeBuilder<ScholarshipApplication> b)
    {
        b.HasIndex(x => x.PublicId).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.StudentEnrollmentId, x.SubmittedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.State, x.SubmittedAtUtc });
        b.HasOne<StudentEnrollment>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentEnrollmentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<DiscountRule>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DiscountRuleId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FileAsset>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.EvidenceFileAssetId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ScholarshipApplication>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.RenewalOfApplicationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<StudentDiscount>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentDiscountId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.SubmittedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_ScholarshipApplications_State",
            "[State] IN ('Submitted','Approved','Rejected','Awarded','Cancelled')"));
    }
}

public sealed class ScholarshipApplicationEventConfiguration : IEntityTypeConfiguration<ScholarshipApplicationEvent>
{
    public void Configure(EntityTypeBuilder<ScholarshipApplicationEvent> b)
    {
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ScholarshipApplicationId, x.Id });
        b.HasOne<ScholarshipApplication>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.ScholarshipApplicationId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CounselingCaseConfiguration : IEntityTypeConfiguration<CounselingCase>
{
    public void Configure(EntityTypeBuilder<CounselingCase> b)
    {
        b.HasIndex(x => x.PublicId).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CampusId, x.State, x.OpenedAtUtc });
        b.HasIndex(x => new { x.TenantId, x.AssignedCounselorUserId, x.State });
        b.HasOne<Student>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.StudentId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Campus>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CampusId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.OpenedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.AssignedCounselorUserId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);
        b.ToTable(t => t.HasCheckConstraint("CK_CounselingCases_State",
            "[State] IN ('Requested','Assigned','Active','Escalated','Closed')"));
    }
}

public sealed class CounselingCaseEventConfiguration : IEntityTypeConfiguration<CounselingCaseEvent>
{
    public void Configure(EntityTypeBuilder<CounselingCaseEvent> b)
    {
        b.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.CounselingCaseId, x.Id });
        b.HasOne<CounselingCase>().WithMany()
            .HasForeignKey(x => new { x.TenantId, x.CounselingCaseId })
            .HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<ApplicationUser>().WithMany()
            .HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
    }
}
