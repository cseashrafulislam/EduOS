using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EduOS.Persistence.Migrations;

partial class EduOSDbContextModelSnapshot
{
    private static void AddGroundedStudySnapshot(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity("EduOS.Core.Entities.Library.StudyEvidenceExcerpt", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<Guid>("PublicId").HasColumnType("uniqueidentifier");
            b.Property<long>("DocumentId").HasColumnType("bigint");
            b.Property<string>("SourceLocator").IsRequired().HasMaxLength(160).HasColumnType("nvarchar(160)");
            b.Property<string>("ExcerptText").IsRequired().HasMaxLength(1200).HasColumnType("nvarchar(1200)");
            b.Property<string>("LanguageCode").IsRequired().HasMaxLength(10).HasColumnType("nvarchar(10)");
            b.Property<string>("StateCode").IsRequired().HasMaxLength(16).HasColumnType("nvarchar(16)");
            b.Property<long>("SubmittedByUserId").HasColumnType("bigint");
            b.Property<long?>("ReviewedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReviewedAtUtc").HasColumnType("datetime2");
            b.Property<string>("ReviewReason").HasMaxLength(300).HasColumnType("nvarchar(300)");
            b.Property<DateTime>("CreatedAt").HasColumnType("datetime2");
            b.Property<DateTime?>("UpdatedAt").HasColumnType("datetime2");
            b.Property<long?>("CreatedBy").HasColumnType("bigint");
            b.Property<long?>("UpdatedBy").HasColumnType("bigint");
            b.Property<bool>("IsDeleted").HasColumnType("bit");
            b.Property<DateTime?>("DeletedAt").HasColumnType("datetime2");
            b.Property<long?>("DeletedBy").HasColumnType("bigint");
            b.Property<byte[]>("RowVersion").IsConcurrencyToken().IsRequired()
                .ValueGeneratedOnAddOrUpdate().HasColumnType("rowversion");
            b.HasKey("Id");
            b.HasAlternateKey("TenantId", "Id");
            b.HasIndex("PublicId").IsUnique();
            b.HasIndex("TenantId", "DocumentId", "StateCode", "LanguageCode", "Id");
            b.HasIndex("TenantId", "DocumentId", "SourceLocator")
                .IsUnique().HasFilter("[IsDeleted] = 0 AND [StateCode] IN ('Pending','Approved')");
            b.ToTable("StudyEvidenceExcerpts", t => t.HasCheckConstraint(
                "CK_StudyEvidenceExcerpts_State", "[StateCode] IN (N'Pending',N'Approved',N'Rejected',N'Revoked')"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.Library.StudyEvidenceExcerpt", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Files.Document", null).WithMany()
                .HasForeignKey("TenantId", "DocumentId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
    }
}
