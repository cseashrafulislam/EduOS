using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduOS.Persistence.Migrations;

// Additive C1 snapshot section; EF's next generated migration can inline it.
partial class EduOSDbContextModelSnapshot
{
    private static void AddAdaptiveStudySnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("EduOS.Core.Entities.LMS.AdaptiveStudyGoal", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("CourseId").HasColumnType("bigint");
            b.Property<long>("CourseSectionId").HasColumnType("bigint");
            b.Property<long>("StudentId").HasColumnType("bigint");
            b.Property<string>("SkillCode").IsRequired().HasMaxLength(64).HasColumnType("nvarchar(64)");
            b.Property<decimal>("TargetPercent").HasColumnType("decimal(5,2)");
            b.Property<string>("TeacherNote").HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long>("SetByUserId").HasColumnType("bigint");
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<long?>("CreatedBy").HasColumnType("bigint");
            b.Property<DateTime?>("UpdatedAt").HasColumnType("datetime2");
            b.Property<long?>("UpdatedBy").HasColumnType("bigint");
            b.Property<bool>("IsDeleted").HasColumnType("bit");
            b.Property<DateTime?>("DeletedAt").HasColumnType("datetime2");
            b.Property<long?>("DeletedBy").HasColumnType("bigint");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().IsRequired()
                .ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.HasKey("Id");
            b.HasAlternateKey("TenantId", "Id");
            b.HasIndex("TenantId", "CourseSectionId");
            b.HasIndex("TenantId", "StudentId");
            b.HasIndex("TenantId", "CourseId", "StudentId", "CourseSectionId", "SkillCode")
                .IsUnique().HasFilter("[IsDeleted] = 0");
            b.ToTable("AdaptiveStudyGoals", t => t.HasCheckConstraint(
                "CK_AdaptiveStudyGoals_Target", "[TargetPercent] >= 0 AND [TargetPercent] <= 100"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.LMS.AdaptiveStudyPlanHistory", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<long>("CourseEnrollmentId").HasColumnType("bigint");
            b.Property<long>("StudentId").HasColumnType("bigint");
            b.Property<int>("AlgorithmVersion").HasColumnType("int");
            b.Property<DateTime>("GeneratedAt").HasColumnType("datetime2");
            b.Property<string>("PlanJson").IsRequired().HasColumnType("nvarchar(max)");
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<long?>("CreatedBy").HasColumnType("bigint");
            b.Property<DateTime?>("UpdatedAt").HasColumnType("datetime2");
            b.Property<long?>("UpdatedBy").HasColumnType("bigint");
            b.Property<bool>("IsDeleted").HasColumnType("bit");
            b.Property<DateTime?>("DeletedAt").HasColumnType("datetime2");
            b.Property<long?>("DeletedBy").HasColumnType("bigint");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().IsRequired()
                .ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.HasKey("Id");
            b.HasAlternateKey("TenantId", "Id");
            b.HasIndex("TenantId", "StudentId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "CourseEnrollmentId", "GeneratedAt");
            b.ToTable("AdaptiveStudyPlanHistories");
        });
        modelBuilder.Entity("EduOS.Core.Entities.LMS.Quiz", b =>
        {
            b.Property<long?>("CourseSectionId").HasColumnType("bigint");
            b.Property<string>("SkillCode").HasMaxLength(64).HasColumnType("nvarchar(64)");
            b.Property<int>("PracticeDifficulty").HasColumnType("int");
            b.HasIndex("TenantId", "CourseSectionId");
            b.HasIndex("TenantId", "CourseId", "CourseSectionId", "SkillCode");
            b.ToTable("Quizzes", t => t.HasCheckConstraint(
                "CK_Quizzes_PracticeDifficulty", "[PracticeDifficulty] BETWEEN 1 AND 3"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.LMS.AdaptiveStudyGoal", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.LMS.Course", null).WithMany()
                .HasForeignKey("TenantId", "CourseId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.LMS.CourseSection", null).WithMany()
                .HasForeignKey("TenantId", "CourseSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Students.Student", null).WithMany()
                .HasForeignKey("TenantId", "StudentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.LMS.AdaptiveStudyPlanHistory", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.LMS.CourseEnrollment", null).WithMany()
                .HasForeignKey("TenantId", "CourseEnrollmentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Students.Student", null).WithMany()
                .HasForeignKey("TenantId", "StudentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.LMS.Quiz", b =>
        {
            b.HasOne("EduOS.Core.Entities.LMS.CourseSection", null).WithMany()
                .HasForeignKey("TenantId", "CourseSectionId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
        });
    }
}
