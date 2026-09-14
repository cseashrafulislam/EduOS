using EduOS.Core.Entities.Admission;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Admission;

public class AdmissionApplicantConfiguration : IEntityTypeConfiguration<AdmissionApplicant>
{
    public void Configure(EntityTypeBuilder<AdmissionApplicant> builder)
    {
        builder.ToTable("AdmissionApplicants");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.ApplicationNumber).IsRequired().HasMaxLength(30);
        builder.Property(x => x.ApplicantName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ApplicantNameBangla).HasMaxLength(200);
        builder.Property(x => x.DateOfBirth).HasColumnType("date");
        builder.Property(x => x.Gender).HasConversion<int>();
        builder.Property(x => x.PrimaryMobile).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Email).HasMaxLength(254);
        builder.Property(x => x.GuardianName).HasMaxLength(200);
        builder.Property(x => x.GuardianRelation).HasMaxLength(50);
        builder.Property(x => x.GuardianMobile).HasMaxLength(20);
        builder.Property(x => x.PresentAddress).HasMaxLength(1000);
        builder.Property(x => x.PermanentAddress).HasMaxLength(1000);
        builder.Property(x => x.PreviousInstitution).HasMaxLength(200);
        builder.Property(x => x.PreferredLanguage).IsRequired().HasMaxLength(10);
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.DecisionNote).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ApplicationNumber }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.SubmittedAtUtc });
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.AcademicUnitId });

        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Campus).WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicUnit).WithMany().HasForeignKey(x => x.AcademicUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}
