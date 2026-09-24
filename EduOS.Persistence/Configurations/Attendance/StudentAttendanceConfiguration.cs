using EduOS.Core.Entities.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EduOS.Persistence.Configurations.Attendance;

public sealed class StudentAttendanceConfiguration : IEntityTypeConfiguration<StudentAttendance>
{
    public void Configure(EntityTypeBuilder<StudentAttendance> builder)
    {
        builder.Property(x => x.Date).HasColumnType("date");
        builder.Property(x => x.Status).IsRequired().HasMaxLength(20);
        builder.Property(x => x.Remarks).HasMaxLength(500);

        builder.HasIndex(x => new { x.TenantId, x.StudentId, x.Date })
            .IsUnique()
            .HasDatabaseName("UX_StudentAttendances_Tenant_Student_Date")
            .HasFilter("[IsDeleted] = 0");
        builder.HasIndex(x => new { x.TenantId, x.ClassId, x.SectionId, x.Date })
            .HasDatabaseName("IX_StudentAttendances_Tenant_Roster_Date");

        builder.ToTable("StudentAttendances", table =>
        {
            table.HasCheckConstraint("CK_StudentAttendances_Status", "[Status] IN ('Present','Absent','Late','Leave')");
            table.HasCheckConstraint("CK_StudentAttendances_TimeRange", "[OutTime] IS NULL OR [InTime] IS NULL OR [OutTime] >= [InTime]");
        });

        builder.HasOne(x => x.Student).WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Class).WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Section).WithMany().HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
    }
}
