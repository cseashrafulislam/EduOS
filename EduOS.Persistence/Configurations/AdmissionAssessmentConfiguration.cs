using EduOS.Core.Entities.Admission;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations;

public sealed class AdmissionTestConfiguration : IEntityTypeConfiguration<AdmissionTest>
{
    public void Configure(EntityTypeBuilder<AdmissionTest> builder)
    {
        builder.ToTable("AdmissionTests");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TotalMarks).HasPrecision(18, 2);
        builder.Property(x => x.PassMarks).HasPrecision(18, 2);
        builder.Property(x => x.Venue).HasMaxLength(200);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.CampusId, x.AcademicUnitId, x.TestDate });

        builder.HasOne(x => x.AcademicYear)
            .WithMany()
            .HasForeignKey(x => x.AcademicYearId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Campus)
            .WithMany()
            .HasForeignKey(x => x.CampusId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.AcademicUnit)
            .WithMany()
            .HasForeignKey(x => x.AcademicUnitId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionResultConfiguration : IEntityTypeConfiguration<AdmissionResult>
{
    public void Configure(EntityTypeBuilder<AdmissionResult> builder)
    {
        builder.ToTable("AdmissionResults");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ObtainedMarks).HasPrecision(18, 2);
        builder.Property(x => x.Percentage).HasPrecision(7, 2);
        builder.Property(x => x.ResultStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Grade).HasMaxLength(20);
        builder.Property(x => x.Remarks).HasMaxLength(500);
        builder.Property(x => x.RowVersion).IsRowVersion().IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.AdmissionTestId, x.ApplicantId })
            .IsUnique()
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.AdmissionTestId, x.MeritPosition });

        builder.HasOne(x => x.AdmissionTest)
            .WithMany()
            .HasForeignKey(x => x.AdmissionTestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Applicant)
            .WithMany()
            .HasForeignKey(x => x.ApplicantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
