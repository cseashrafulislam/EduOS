using EduOS.Core.Entities.Academic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Academic;

public sealed class AcademicCalendarPolicyConfiguration : IEntityTypeConfiguration<AcademicCalendarPolicy>
{
    public void Configure(EntityTypeBuilder<AcademicCalendarPolicy> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_AcademicCalendarPolicies_Tenant_Request");
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.CampusId })
            .IsUnique().HasDatabaseName("UX_AcademicCalendarPolicies_Tenant_Scope")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1");
        builder.HasOne(x => x.AcademicYear).WithMany().HasForeignKey(x => x.AcademicYearId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Campus).WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class AcademicCalendarEventConfiguration : IEntityTypeConfiguration<AcademicCalendarEvent>
{
    public void Configure(EntityTypeBuilder<AcademicCalendarEvent> builder)
    {
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.ClientRequestId })
            .IsUnique().HasDatabaseName("UX_AcademicCalendarEvents_Tenant_Request")
            .HasFilter("[ClientRequestId] IS NOT NULL");
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.CampusId, x.StartDate, x.EndDate });
        builder.HasIndex(x => new { x.TenantId, x.AcademicYearId, x.AcademicTermId, x.CampusId, x.Title, x.StartDate, x.EndDate })
            .IsUnique().HasDatabaseName("UX_AcademicCalendarEvents_Tenant_Scope_Title_Dates")
            .HasFilter("[IsDeleted] = 0 AND [IsActive] = 1 AND [AcademicYearId] IS NOT NULL");
        builder.HasOne(x => x.Campus).WithMany().HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.Restrict);
    }
}
