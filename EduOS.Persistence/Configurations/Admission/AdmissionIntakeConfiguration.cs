using EduOS.Core.Entities.Admission;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Admission;

public sealed class AdmissionIntakeFormConfiguration : IEntityTypeConfiguration<AdmissionIntakeForm>
{
    public void Configure(EntityTypeBuilder<AdmissionIntakeForm> builder)
    {
        builder.ToTable("AdmissionIntakeForms", table =>
        {
            table.HasCheckConstraint("CK_AdmissionIntakeForms_DateRange", "[ClosesAtUtc] > [OpensAtUtc]");
            table.HasCheckConstraint("CK_AdmissionIntakeForms_Fee", "[ApplicationFee] >= 0");
            table.HasCheckConstraint("CK_AdmissionIntakeForms_Status", "[Status] >= 1 AND [Status] <= 4");
        });
        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.ApplicationFee).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.FieldsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.DocumentRequirementsJson).HasColumnType("nvarchar(max)").IsRequired();
        builder.Property(x => x.Status).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.Status, x.OpensAtUtc, x.ClosesAtUtc });

        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Campus).WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicUnit).WithMany().HasForeignKey(x => x.AcademicUnitId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AdmissionApplicantDocumentConfiguration : IEntityTypeConfiguration<AdmissionApplicantDocument>
{
    public void Configure(EntityTypeBuilder<AdmissionApplicantDocument> builder)
    {
        builder.ToTable("AdmissionApplicantDocuments", table =>
        {
            table.HasCheckConstraint("CK_AdmissionApplicantDocuments_FileSize", "[FileSizeBytes] > 0");
            table.HasCheckConstraint("CK_AdmissionApplicantDocuments_Status", "[VerificationStatus] >= 1 AND [VerificationStatus] <= 3");
        });
        builder.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OriginalFileName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StorageKey).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Sha256).HasMaxLength(44).IsRequired();
        builder.Property(x => x.VerificationStatus).HasConversion<int>();
        builder.Property(x => x.ReviewNote).HasMaxLength(1000);
        builder.Property(x => x.RowVersion).IsRowVersion();

        builder.HasIndex(x => x.PublicId).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.ApplicantId, x.DocumentType })
            .IsUnique().HasDatabaseName("UX_AdmissionApplicantDocuments_CurrentType")
            .HasFilter("[IsDeleted] = 0 AND [IsCurrent] = 1");
        builder.HasIndex(x => new { x.TenantId, x.ApplicantId, x.VerificationStatus, x.IsCurrent });

        builder.HasOne(x => x.Applicant).WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AdmissionIntakeForm).WithMany().HasForeignKey(x => x.AdmissionIntakeFormId).OnDelete(DeleteBehavior.Restrict);
    }
}
