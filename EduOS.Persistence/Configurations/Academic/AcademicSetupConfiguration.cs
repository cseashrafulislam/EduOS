using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Academic;

public sealed class AcademicProgramConfiguration : IEntityTypeConfiguration<AcademicProgram>
{
    public void Configure(EntityTypeBuilder<AcademicProgram> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique().HasDatabaseName("UX_AcademicPrograms_Tenant_Code")
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcademicLevelConfiguration : IEntityTypeConfiguration<AcademicLevel>
{
    public void Configure(EntityTypeBuilder<AcademicLevel> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AcademicProgramId, x.Code })
            .IsUnique().HasDatabaseName("UX_AcademicLevels_Tenant_Program_Code")
            .HasFilter("[IsDeleted] = 0");
    }
}

public sealed class CanonicalSubjectConfiguration : IEntityTypeConfiguration<Subject>
{
    public void Configure(EntityTypeBuilder<Subject> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique().HasDatabaseName("UX_Subjects_Tenant_CanonicalCode")
            .HasFilter("[IsDeleted] = 0 AND [ClassId] IS NULL");
    }
}

public sealed class AcademicCurriculumConfiguration : IEntityTypeConfiguration<AcademicCurriculum>
{
    public void Configure(EntityTypeBuilder<AcademicCurriculum> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique().HasDatabaseName("UX_AcademicCurriculums_Tenant_Code")
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.AcademicProgramId })
            .IsUnique().HasDatabaseName("UX_AcademicCurriculums_Tenant_CurrentProgram")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [IsCurrent] = 1");
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.EffectiveFromAcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AcademicYear>().WithMany().HasForeignKey(x => x.EffectiveToAcademicYearId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class CurriculumSubjectConfiguration : IEntityTypeConfiguration<CurriculumSubject>
{
    public void Configure(EntityTypeBuilder<CurriculumSubject> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AcademicCurriculumId, x.AcademicLevelId, x.SubjectId, x.AcademicTrackId, x.MediumId })
            .IsUnique().HasDatabaseName("UX_CurriculumSubjects_Tenant_Scope")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");
    }
}

public sealed class AcademicBatchConfiguration : IEntityTypeConfiguration<AcademicBatch>
{
    public void Configure(EntityTypeBuilder<AcademicBatch> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.Code })
            .IsUnique().HasDatabaseName("UX_AcademicBatches_Tenant_Year_Code")
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcademicRoomConfiguration : IEntityTypeConfiguration<Room>
{
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code })
            .IsUnique().HasDatabaseName("UX_Rooms_Tenant_Code")
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class ProgramCampusConfiguration : IEntityTypeConfiguration<ProgramCampus>
{
    public void Configure(EntityTypeBuilder<ProgramCampus> builder)
    {
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AcademicProgramId, x.CampusId })
            .IsUnique().HasDatabaseName("UX_ProgramCampuses_Tenant_Program_Campus")
            .HasFilter("[IsDeleted] = 0");
        builder.HasOne<Campus>().WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}
