using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Academic;

public sealed class StudentEnrollmentConfiguration : IEntityTypeConfiguration<StudentEnrollment>
{
    public void Configure(EntityTypeBuilder<StudentEnrollment> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_StudentEnrollments_Tenant_Request")
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasDatabaseName("UX_StudentEnrollments_Tenant_CurrentStudent")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");
        builder.HasIndex(x => new { x.TenantId, x.AcademicBatchId, x.RollNo })
            .IsUnique().HasDatabaseName("UX_StudentEnrollments_Tenant_Batch_Roll")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");

        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Campus).WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicProgram).WithMany().HasForeignKey(x => x.AcademicProgramId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicLevel).WithMany().HasForeignKey(x => x.AcademicLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicBatch).WithMany().HasForeignKey(x => x.AcademicBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicCurriculum).WithMany().HasForeignKey(x => x.AcademicCurriculumId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Medium).WithMany().HasForeignKey(x => x.MediumId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTrack).WithMany().HasForeignKey(x => x.AcademicTrackId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class StudentSubjectRegistrationConfiguration : IEntityTypeConfiguration<StudentSubjectRegistration>
{
    public void Configure(EntityTypeBuilder<StudentSubjectRegistration> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_StudentSubjectRegistrations_Tenant_Request")
            .HasFilter("[IsDeleted] = 0 AND [ClientRequestId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.StudentEnrollmentId, x.SubjectId })
            .IsUnique().HasDatabaseName("UX_StudentSubjectRegistrations_Tenant_Enrollment_Subject")
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.Status });

        builder.HasOne(x => x.StudentEnrollment).WithMany(x => x.SubjectRegistrations).HasForeignKey(x => x.StudentEnrollmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicBatch).WithMany().HasForeignKey(x => x.AcademicBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicCurriculum).WithMany().HasForeignKey(x => x.AcademicCurriculumId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CurriculumSubject).WithMany().HasForeignKey(x => x.CurriculumSubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
    }
}
