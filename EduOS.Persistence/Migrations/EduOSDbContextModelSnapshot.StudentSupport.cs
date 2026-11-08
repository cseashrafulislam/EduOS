using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduOS.Persistence.Migrations;

// The next generated EF snapshot will inline this additive 2026-11-08 model section.
partial class EduOSDbContextModelSnapshot
{
    private static void AddStudentSupportSnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipApplication", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<Guid>("PublicId").HasColumnType("uniqueidentifier");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<long>("StudentEnrollmentId").HasColumnType("bigint");
            b.Property<long>("DiscountRuleId").HasColumnType("bigint");
            b.Property<long?>("EvidenceFileAssetId").HasColumnType("bigint");
            b.Property<long?>("RenewalOfApplicationId").HasColumnType("bigint");
            b.Property<DateOnly>("EffectiveFrom").HasColumnType("date");
            b.Property<DateOnly?>("EffectiveTo").HasColumnType("date");
            b.Property<string>("EligibilityStatement").IsRequired().HasMaxLength(700).HasColumnType("nvarchar(700)");
            b.Property<string>("State").IsRequired().HasMaxLength(20).HasColumnType("nvarchar(20)");
            b.Property<long>("SubmittedByUserId").HasColumnType("bigint");
            b.Property<DateTime>("SubmittedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("ReviewedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReviewedAtUtc").HasColumnType("datetime2");
            b.Property<string>("DecisionReason").HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long?>("StudentDiscountId").HasColumnType("bigint");
            b.Property<DateTime?>("AwardedAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("PublicId").IsUnique();
            b.HasIndex("SubmittedByUserId");
            b.HasIndex("ReviewedByUserId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "StudentEnrollmentId", "SubmittedAtUtc");
            b.HasIndex("TenantId", "State", "SubmittedAtUtc");
            b.HasIndex("TenantId", "DiscountRuleId");
            b.HasIndex("TenantId", "EvidenceFileAssetId");
            b.HasIndex("TenantId", "RenewalOfApplicationId");
            b.HasIndex("TenantId", "StudentDiscountId");
            b.ToTable("ScholarshipApplications", t => t.HasCheckConstraint(
                "CK_ScholarshipApplications_State",
                "[State] IN ('Submitted','Approved','Rejected','Awarded','Cancelled')"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipApplicationEvent", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("ScholarshipApplicationId").HasColumnType("bigint");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<string>("Action").IsRequired().HasMaxLength(24).HasColumnType("nvarchar(24)");
            b.Property<string>("Reason").IsRequired().HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long>("ActorUserId").HasColumnType("bigint");
            b.Property<DateTime>("OccurredAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("ActorUserId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "ScholarshipApplicationId", "Id");
            b.ToTable("ScholarshipApplicationEvents");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCase", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<Guid>("PublicId").HasColumnType("uniqueidentifier");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<long>("StudentId").HasColumnType("bigint");
            b.Property<long>("CampusId").HasColumnType("bigint");
            b.Property<string>("State").IsRequired().HasMaxLength(20).HasColumnType("nvarchar(20)");
            b.Property<bool>("IsUrgent").HasColumnType("bit");
            b.Property<long>("OpenedByUserId").HasColumnType("bigint");
            b.Property<DateTime>("OpenedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("AssignedCounselorUserId").HasColumnType("bigint");
            b.Property<long?>("AssignedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("AssignedAtUtc").HasColumnType("datetime2");
            b.Property<DateTime?>("ClosedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("ClosedByUserId").HasColumnType("bigint");
            b.Property<string>("ProtectedSummary").IsRequired().HasMaxLength(8000).HasColumnType("nvarchar(max)");
            b.Property<bool>("GuardianContactConsented").HasColumnType("bit");
            b.Property<DateTime?>("GuardianConsentAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("PublicId").IsUnique();
            b.HasIndex("OpenedByUserId");
            b.HasIndex("AssignedCounselorUserId");
            b.HasIndex("AssignedByUserId");
            b.HasIndex("ClosedByUserId");
            b.HasIndex("TenantId", "StudentId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "CampusId", "State", "OpenedAtUtc");
            b.HasIndex("TenantId", "AssignedCounselorUserId", "State");
            b.ToTable("CounselingCases", t => t.HasCheckConstraint("CK_CounselingCases_State",
                "[State] IN ('Requested','Assigned','Active','Escalated','Closed')"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCaseEvent", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("CounselingCaseId").HasColumnType("bigint");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<string>("Action").IsRequired().HasMaxLength(24).HasColumnType("nvarchar(24)");
            b.Property<string>("ProtectedNote").IsRequired().HasMaxLength(8000).HasColumnType("nvarchar(max)");
            b.Property<DateTime?>("AppointmentAtUtc").HasColumnType("datetime2");
            b.Property<long>("ActorUserId").HasColumnType("bigint");
            b.Property<DateTime>("OccurredAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("ActorUserId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "CounselingCaseId", "Id");
            b.ToTable("CounselingCaseEvents");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipApplication", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("SubmittedByUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ReviewedByUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Academic.StudentEnrollment", null).WithMany()
                .HasForeignKey("TenantId", "StudentEnrollmentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Finance.DiscountRule", null).WithMany()
                .HasForeignKey("TenantId", "DiscountRuleId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Files.FileAsset", null).WithMany()
                .HasForeignKey("TenantId", "EvidenceFileAssetId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Finance.ScholarshipApplication", null).WithMany()
                .HasForeignKey("TenantId", "RenewalOfApplicationId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Finance.StudentDiscount", null).WithMany()
                .HasForeignKey("TenantId", "StudentDiscountId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipApplicationEvent", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ActorUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Finance.ScholarshipApplication", null).WithMany()
                .HasForeignKey("TenantId", "ScholarshipApplicationId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCase", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Students.Student", null).WithMany()
                .HasForeignKey("TenantId", "StudentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.SaaS.Campus", null).WithMany()
                .HasForeignKey("TenantId", "CampusId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("OpenedByUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("AssignedCounselorUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("AssignedByUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ClosedByUserId").OnDelete(DeleteBehavior.Restrict);
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCaseEvent", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ActorUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Students.CounselingCase", null).WithMany()
                .HasForeignKey("TenantId", "CounselingCaseId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
    }
}
