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
            b.Property<long?>("DiscountRuleId").HasColumnType("bigint");
            b.Property<string>("AwardMode").IsRequired().HasMaxLength(12).HasColumnType("nvarchar(12)");
            b.Property<decimal?>("CashAmount").HasColumnType("decimal(18,2)");
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
            b.ToTable("ScholarshipApplications", t =>
            {
                t.HasCheckConstraint("CK_ScholarshipApplications_State",
                    "[State] IN ('Submitted','Approved','Rejected','Awarded','Cancelled')");
                t.HasCheckConstraint("CK_ScholarshipApplications_Benefit",
                    "([AwardMode]='FeeCredit' AND [DiscountRuleId] IS NOT NULL AND [CashAmount] IS NULL) OR " +
                    "([AwardMode]='CashGrant' AND [DiscountRuleId] IS NULL AND [CashAmount]>0 AND [StudentDiscountId] IS NULL)");
            });
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
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipDisbursement", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<Guid>("PublicId").HasColumnType("uniqueidentifier");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<Guid?>("ReviewRequestId").HasColumnType("uniqueidentifier");
            b.Property<long>("ScholarshipApplicationId").HasColumnType("bigint");
            b.Property<decimal>("Amount").HasColumnType("decimal(18,2)");
            b.Property<string>("Method").IsRequired().HasMaxLength(8).HasColumnType("nvarchar(8)");
            b.Property<string>("BeneficiaryType").IsRequired().HasMaxLength(8).HasColumnType("nvarchar(8)");
            b.Property<long?>("GuardianId").HasColumnType("bigint");
            b.Property<string>("BeneficiaryNameSnapshot").IsRequired().HasMaxLength(200).HasColumnType("nvarchar(200)");
            b.Property<long>("ExpenseAccountId").HasColumnType("bigint");
            b.Property<long>("PaymentAccountId").HasColumnType("bigint");
            b.Property<long?>("BankAccountId").HasColumnType("bigint");
            b.Property<long>("AccountingPeriodId").HasColumnType("bigint");
            b.Property<DateOnly>("PaymentDate").HasColumnType("date");
            b.Property<string>("ExternalPaymentReference").IsRequired().HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("State").IsRequired().HasMaxLength(24).HasColumnType("nvarchar(24)");
            b.Property<long>("PreparedByUserId").HasColumnType("bigint");
            b.Property<DateTime>("SubmittedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("ReviewedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReviewedAtUtc").HasColumnType("datetime2");
            b.Property<string>("ReviewReason").HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long?>("JournalId").HasColumnType("bigint");
            b.Property<long?>("ReversalJournalId").HasColumnType("bigint");
            b.Property<Guid?>("ReversalRequestId").HasColumnType("uniqueidentifier");
            b.Property<Guid?>("ReversalReviewRequestId").HasColumnType("uniqueidentifier");
            b.Property<long?>("ReversalRequestedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReversalRequestedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("ReversalAccountingPeriodId").HasColumnType("bigint");
            b.Property<DateOnly?>("ReversalDate").HasColumnType("date");
            b.Property<string>("ExternalReversalReference").HasMaxLength(100).HasColumnType("nvarchar(100)");
            b.Property<string>("ReversalReason").HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long?>("ReversedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReversedAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "ReviewRequestId").IsUnique()
                .HasFilter("[ReviewRequestId] IS NOT NULL");
            b.HasIndex("TenantId", "ScholarshipApplicationId").IsUnique()
                .HasFilter("[State] <> N'Rejected' AND [IsDeleted] = 0");
            b.HasIndex("TenantId", "Method", "PaymentAccountId", "ExternalPaymentReference").IsUnique();
            foreach (var column in new[] { "GuardianId", "ExpenseAccountId", "PaymentAccountId",
                "BankAccountId", "AccountingPeriodId", "ReversalAccountingPeriodId", "JournalId", "ReversalJournalId" })
                b.HasIndex("TenantId", column);
            foreach (var column in new[] { "PreparedByUserId", "ReviewedByUserId", "ReversalRequestedByUserId", "ReversedByUserId" })
                b.HasIndex(column);
            b.ToTable("ScholarshipDisbursements", t =>
            {
                t.HasCheckConstraint("CK_ScholarshipDisbursements_State",
                    "[State] IN ('Submitted','Posted','Rejected','ReversalRequested','Reversed')");
                t.HasCheckConstraint("CK_ScholarshipDisbursements_Amount", "[Amount] > 0 AND [Amount] <= 99999999");
                t.HasCheckConstraint("CK_ScholarshipDisbursements_Method",
                    "([Method]='Cash' AND [BankAccountId] IS NULL) OR ([Method]='Bank' AND [BankAccountId] IS NOT NULL)");
                t.HasCheckConstraint("CK_ScholarshipDisbursements_Beneficiary",
                    "([BeneficiaryType]='Student' AND [GuardianId] IS NULL) OR " +
                    "([BeneficiaryType]='Guardian' AND [GuardianId] IS NOT NULL)");
            });
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
            b.Property<Guid?>("RedactionRequestId").HasColumnType("uniqueidentifier");
            b.Property<long?>("RedactionRequestedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("RedactionRequestedAtUtc").HasColumnType("datetime2");
            b.Property<DateTime?>("RedactedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("RedactedByUserId").HasColumnType("bigint");
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
            b.HasIndex("RedactionRequestedByUserId");
            b.HasIndex("RedactedByUserId");
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
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCampusPolicy", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<long>("CampusId").HasColumnType("bigint");
            b.Property<int>("RetentionDays").HasColumnType("int");
            b.Property<bool>("LegalHold").HasColumnType("bit");
            b.Property<string>("EmergencyGuidanceEn").IsRequired().HasMaxLength(350).HasColumnType("nvarchar(350)");
            b.Property<string>("EmergencyGuidanceBn").IsRequired().HasMaxLength(350).HasColumnType("nvarchar(350)");
            b.Property<long>("ApprovedByUserId").HasColumnType("bigint");
            b.Property<DateTime>("ApprovedAtUtc").HasColumnType("datetime2");
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
            b.HasIndex("ApprovedByUserId");
            b.HasIndex("TenantId", "CampusId").IsUnique();
            b.ToTable("CounselingCampusPolicies", t => t.HasCheckConstraint(
                "CK_CounselingCampusPolicy_Retention", "[RetentionDays] BETWEEN 30 AND 3650"));
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingPolicyChange", b =>
        {
            b.Property<long>("Id").ValueGeneratedOnAdd().HasColumnType("bigint");
            SqlServerPropertyBuilderExtensions.UseIdentityColumn(b.Property<long>("Id"));
            b.Property<long>("TenantId").HasColumnType("bigint");
            b.Property<Guid>("PublicId").HasColumnType("uniqueidentifier");
            b.Property<Guid>("ClientRequestId").HasColumnType("uniqueidentifier");
            b.Property<Guid?>("ReviewRequestId").HasColumnType("uniqueidentifier");
            b.Property<long>("CampusId").HasColumnType("bigint");
            b.Property<string>("State").IsRequired().HasMaxLength(12).HasColumnType("nvarchar(12)");
            b.Property<int>("RetentionDays").HasColumnType("int");
            b.Property<bool>("LegalHold").HasColumnType("bit");
            b.Property<string>("EmergencyGuidanceEn").IsRequired().HasMaxLength(350).HasColumnType("nvarchar(350)");
            b.Property<string>("EmergencyGuidanceBn").IsRequired().HasMaxLength(350).HasColumnType("nvarchar(350)");
            b.Property<byte[]>("ExpectedPolicyVersion").HasMaxLength(8).HasColumnType("varbinary(8)");
            b.Property<string>("Reason").IsRequired().HasMaxLength(500).HasColumnType("nvarchar(500)");
            b.Property<long>("ProposedByUserId").HasColumnType("bigint");
            b.Property<DateTime>("ProposedAtUtc").HasColumnType("datetime2");
            b.Property<long?>("ReviewedByUserId").HasColumnType("bigint");
            b.Property<DateTime?>("ReviewedAtUtc").HasColumnType("datetime2");
            b.Property<string>("ReviewReason").HasMaxLength(500).HasColumnType("nvarchar(500)");
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
            b.HasIndex("ProposedByUserId");
            b.HasIndex("ReviewedByUserId");
            b.HasIndex("TenantId", "ClientRequestId").IsUnique();
            b.HasIndex("TenantId", "ReviewRequestId").IsUnique()
                .HasFilter("[ReviewRequestId] IS NOT NULL");
            b.HasIndex("TenantId", "CampusId").IsUnique()
                .HasFilter("[State] = N'Submitted' AND [IsDeleted] = 0");
            b.ToTable("CounselingPolicyChanges", t =>
            {
                t.HasCheckConstraint("CK_CounselingPolicyChange_Retention", "[RetentionDays] BETWEEN 30 AND 3650");
                t.HasCheckConstraint("CK_CounselingPolicyChange_State", "[State] IN ('Submitted','Approved','Rejected')");
                t.HasCheckConstraint("CK_CounselingPolicyChange_Reviewer",
                    "([State]='Submitted' AND [ReviewedByUserId] IS NULL AND [ReviewedAtUtc] IS NULL AND [ReviewRequestId] IS NULL) OR " +
                    "([State]<>'Submitted' AND [ReviewedByUserId] IS NOT NULL AND [ReviewedAtUtc] IS NOT NULL AND " +
                    "[ReviewRequestId] IS NOT NULL AND [ReviewedByUserId]<>[ProposedByUserId])");
            });
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
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity("EduOS.Core.Entities.Finance.ScholarshipDisbursement", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            foreach (var fk in new[] { ("EduOS.Core.Entities.Finance.ScholarshipApplication", "ScholarshipApplicationId"),
                ("EduOS.Core.Entities.Students.Guardian", "GuardianId"),
                ("EduOS.Core.Entities.Accounting.Account", "ExpenseAccountId"),
                ("EduOS.Core.Entities.Accounting.Account", "PaymentAccountId"),
                ("EduOS.Core.Entities.Accounting.BankAccount", "BankAccountId"),
                ("EduOS.Core.Entities.Accounting.AccountingPeriod", "AccountingPeriodId"),
                ("EduOS.Core.Entities.Accounting.AccountingPeriod", "ReversalAccountingPeriodId"),
                ("EduOS.Core.Entities.Accounting.Journal", "JournalId"),
                ("EduOS.Core.Entities.Accounting.Journal", "ReversalJournalId") })
                b.HasOne(fk.Item1, null).WithMany().HasForeignKey("TenantId", fk.Item2)
                    .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict);
            foreach (var column in new[] { "PreparedByUserId", "ReviewedByUserId",
                "ReversalRequestedByUserId", "ReversedByUserId" })
                b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                    .HasForeignKey(column).OnDelete(DeleteBehavior.Restrict);
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
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("RedactionRequestedByUserId").OnDelete(DeleteBehavior.Restrict);
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("RedactedByUserId").OnDelete(DeleteBehavior.Restrict);
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
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingCampusPolicy", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.SaaS.Campus", null).WithMany()
                .HasForeignKey("TenantId", "CampusId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ApprovedByUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.Navigation("Tenant");
        });
        modelBuilder.Entity("EduOS.Core.Entities.Students.CounselingPolicyChange", b =>
        {
            b.HasOne("EduOS.Core.Entities.SaaS.Tenant", "Tenant").WithMany()
                .HasForeignKey("TenantId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.SaaS.Campus", null).WithMany()
                .HasForeignKey("TenantId", "CampusId")
                .HasPrincipalKey("TenantId", "Id").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ProposedByUserId").OnDelete(DeleteBehavior.Restrict).IsRequired();
            b.HasOne("EduOS.Core.Entities.Auth.ApplicationUser", null).WithMany()
                .HasForeignKey("ReviewedByUserId").OnDelete(DeleteBehavior.Restrict);
            b.Navigation("Tenant");
        });
        AddStudentInvoiceDiscountAttributionSnapshot(modelBuilder);
    }
}
