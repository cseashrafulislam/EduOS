using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Academic;

public sealed class SubstitutionConfiguration : IEntityTypeConfiguration<Substitution>
{
    public void Configure(EntityTypeBuilder<Substitution> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_Substitutions_Tenant_Request")
            .HasFilter("[ClientRequestId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.RoutineEntryId, x.Date })
            .IsUnique().HasDatabaseName("UX_Substitutions_Tenant_Routine_Date")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [RoutineEntryId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.SubstituteTeacherId, x.Date, x.RoutineTimeSlotId })
            .IsUnique().HasDatabaseName("UX_Substitutions_Tenant_Substitute_Date_Slot")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [RoutineTimeSlotId] IS NOT NULL");
        builder.ToTable("Substitutions", x => x.HasCheckConstraint("CK_Substitutions_CancellationState", "([IsActive] = 1 AND [CancelledAt] IS NULL AND [CancelledBy] IS NULL AND [CancellationReason] IS NULL) OR ([IsActive] = 0 AND [CancelledAt] IS NOT NULL AND [CancelledBy] IS NOT NULL AND [CancellationReason] IS NOT NULL)"));

        builder.HasOne(x => x.OriginalTeacher).WithMany().HasForeignKey(x => x.OriginalTeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SubstituteTeacher).WithMany().HasForeignKey(x => x.SubstituteTeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RoutineEntry).WithMany().HasForeignKey(x => x.RoutineEntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicBatch).WithMany().HasForeignKey(x => x.AcademicBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.RoutineTimeSlot).WithMany().HasForeignKey(x => x.RoutineTimeSlotId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class LessonPlanConfiguration : IEntityTypeConfiguration<LessonPlan>
{
    public void Configure(EntityTypeBuilder<LessonPlan> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_LessonPlans_Tenant_Request")
            .HasFilter("[ClientRequestId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.NaturalKey })
            .IsUnique().HasDatabaseName("UX_LessonPlans_Tenant_NaturalKey")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [NaturalKey] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.AcademicBatchId, x.TeacherId, x.StartDate, x.EndDate, x.Status });
        builder.ToTable("LessonPlans", x =>
        {
            x.HasCheckConstraint("CK_LessonPlans_ProgressPercent", "[ProgressPercent] >= 0 AND [ProgressPercent] <= 100");
            x.HasCheckConstraint("CK_LessonPlans_CanonicalState", "[InstructorAssignmentId] IS NULL OR (([Status] IN ('Draft','Submitted','Approved','Rejected') AND [ProgressPercent] = 0) OR ([Status] = 'InProgress' AND [ProgressPercent] BETWEEN 1 AND 99) OR ([Status] = 'Completed' AND [ProgressPercent] = 100))");
        });

        builder.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Subject).WithMany().HasForeignKey(x => x.SubjectId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Teacher).WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.InstructorAssignment).WithMany().HasForeignKey(x => x.InstructorAssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicBatch).WithMany().HasForeignKey(x => x.AcademicBatchId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AcademicTerm).WithMany().HasForeignKey(x => x.AcademicTermId).OnDelete(DeleteBehavior.Restrict);
    }
}
