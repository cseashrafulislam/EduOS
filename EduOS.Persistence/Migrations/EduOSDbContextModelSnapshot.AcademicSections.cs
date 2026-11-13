using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduOS.Persistence.Migrations;

// Model snapshot parity for the additive AcademicSectionFoundation migration.
// This is snapshot metadata only: it does not modify data or apply schema changes.
partial class EduOSDbContextModelSnapshot
{
    private static void AddAcademicSectionsSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("EduOS.Core.Entities.Academic.AcademicSection", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("CampusId").HasColumnType("bigint");
            b.Property<long>("AcademicLevelId").HasColumnType("bigint");
            b.Property<int>("Capacity").HasColumnType("int");
            b.Property<Guid?>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<string>("Code").IsRequired().HasMaxLength(50).HasColumnType("nvarchar(50)");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<long?>("CreatedBy").HasColumnType("bigint");
            b.Property<DateTime?>("DeletedAt").HasColumnType("datetime2");
            b.Property<long?>("DeletedBy").HasColumnType("bigint");
            b.Property<int>("DisplayOrder").HasColumnType("int");
            b.Property<bool>("IsActive").HasColumnType("bit");
            b.Property<bool>("IsDeleted").HasColumnType("bit");
            b.Property<string>("Name").IsRequired().HasMaxLength(150).HasColumnType("nvarchar(150)");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().IsRequired()
                .ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<DateTime?>("UpdatedAt").HasColumnType("datetime2");
            b.Property<long?>("UpdatedBy").HasColumnType("bigint");
            b.HasKey("Id");
            b.HasIndex("TenantId", "CampusId", "AcademicLevelId", "Code")
                .IsUnique().HasFilter("[IsDeleted] = 0")
                .HasDatabaseName("UX_AcademicSections_Campus_Level_Code");
            b.HasIndex("TenantId", "CampusId");
            b.HasIndex("TenantId", "AcademicLevelId");
            b.HasIndex("TenantId", "ClientRequestId")
                .IsUnique().HasFilter("[ClientRequestId] IS NOT NULL");
            b.ToTable("AcademicSections", t =>
                t.HasCheckConstraint("CK_AcademicSections_Capacity", "[Capacity] BETWEEN 0 AND 100000"));
        });

        modelBuilder.Entity("EduOS.Core.Entities.Academic.AttendanceSession", b =>
        {
            b.Property<long?>("AcademicSectionId").HasColumnType("bigint");
            b.HasIndex("TenantId", "AcademicSectionId");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Academic.StudentEnrollment", b =>
        {
            b.Property<long?>("AcademicSectionId").HasColumnType("bigint");
            b.HasIndex("TenantId", "AcademicSectionId");
            // Required for the remaining optional Batch FK lookup after
            // removing the invalid legacy Batch/Roll uniqueness mapping.
            b.HasIndex("TenantId", "AcademicBatchId");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Academic.SubjectOffering", b =>
        {
            b.Property<long?>("AcademicSectionId").HasColumnType("bigint");
            b.HasIndex("TenantId", "AcademicSectionId");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Assessment.ResultPublication", b =>
        {
            b.Property<long?>("AcademicSectionId").HasColumnType("bigint");
            b.HasIndex("TenantId", "AcademicSectionId");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.FeeStructure", b =>
        {
            b.Property<long?>("AcademicSectionId").HasColumnType("bigint");
            b.HasIndex("TenantId", "AcademicSectionId");
        });

        modelBuilder.Entity("EduOS.Core.Entities.Academic.AcademicSection", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant")
                .WithMany().HasForeignKey("TenantId")
                .OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.SaaS.Campus", null)
                .WithMany().HasForeignKey("TenantId", "CampusId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Academic.AcademicLevel", null)
                .WithMany().HasForeignKey("TenantId", "AcademicLevelId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Academic.AttendanceSession", b =>
        {
            b.HasOne("EduOS.Core.Entities.Academic.AcademicSection", null)
                .WithMany().HasForeignKey("TenantId", "AcademicSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity("EduOS.Core.Entities.Academic.StudentEnrollment", b =>
        {
            b.HasOne("EduOS.Core.Entities.Academic.AcademicSection", null)
                .WithMany().HasForeignKey("TenantId", "AcademicSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity("EduOS.Core.Entities.Academic.SubjectOffering", b =>
        {
            b.HasOne("EduOS.Core.Entities.Academic.AcademicSection", null)
                .WithMany().HasForeignKey("TenantId", "AcademicSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity("EduOS.Core.Entities.Assessment.ResultPublication", b =>
        {
            b.HasOne("EduOS.Core.Entities.Academic.AcademicSection", null)
                .WithMany().HasForeignKey("TenantId", "AcademicSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.FeeStructure", b =>
        {
            b.HasOne("EduOS.Core.Entities.Academic.AcademicSection", null)
                .WithMany().HasForeignKey("TenantId", "AcademicSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
        // The 20261113190000 migration made AcademicBatchId optional in these
        // four tables. The main snapshot predates that change and still marks
        // its composite relationships required; the live model does not.
        // Align relationship metadata without adding a fictitious migration
        // or changing the tenant-scoped principal key.
        foreach (var entityTypeName in new[]
        {
            "EduOS.Core.Entities.Academic.StudentEnrollment",
            "EduOS.Core.Entities.Academic.SubjectOffering",
            "EduOS.Core.Entities.Academic.AttendanceSession",
            "EduOS.Core.Entities.Assessment.ResultPublication"
        })
        {
            modelBuilder.Entity(entityTypeName)
                .HasOne("EduOS.Core.Entities.Academic.AcademicBatch", null)
                .WithMany()
                .HasForeignKey("TenantId", "AcademicBatchId")
                .HasPrincipalKey("TenantId", "Id")
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }

    }
}
