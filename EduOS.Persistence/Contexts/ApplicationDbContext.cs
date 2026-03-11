using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.SaaS;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Contexts
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser, ApplicationRole, int>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // =========================
        // Auth / Permission
        // =========================
        public DbSet<AppPage> AppPages => Set<AppPage>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePagePermission> RolePagePermissions => Set<RolePagePermission>();
        public DbSet<UserPagePermission> UserPagePermissions => Set<UserPagePermission>();

        // =========================
        // SaaS
        // =========================
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantUser> TenantUsers { get; set; }
        public DbSet<Feature> Features => Set<Feature>();
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
        public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
        public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();
        public DbSet<UsageSnapshot> UsageSnapshots => Set<UsageSnapshot>();
        public DbSet<BillingInvoice> BillingInvoices => Set<BillingInvoice>();
        public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
        public DbSet<OnboardingProgress> OnboardingProgresses => Set<OnboardingProgress>();
        public DbSet<AcademicYear> AcademicYears { get; set; }
        public DbSet<AcademicTerm> AcademicTerms { get; set; }
        public DbSet<Campus> Campus { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // =========================
            // AppPage
            // =========================
            builder.Entity<AppPage>(entity =>
            {
                entity.ToTable("AppPages");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Url).HasMaxLength(250).IsRequired();
                entity.Property(x => x.GroupName).HasMaxLength(100);

                entity.HasIndex(x => x.Code).IsUnique();
            });

            // =========================
            // Permission
            // =========================
            builder.Entity<Permission>(entity =>
            {
                entity.ToTable("Permissions");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();

                entity.HasIndex(x => x.Code).IsUnique();
            });

            // =========================
            // RolePagePermission
            // =========================
            builder.Entity<RolePagePermission>(entity =>
            {
                entity.ToTable("RolePagePermissions");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.Role)
                    .WithMany()
                    .HasForeignKey(x => x.RoleId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.AppPage)
                    .WithMany()
                    .HasForeignKey(x => x.AppPageId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Permission)
                    .WithMany()
                    .HasForeignKey(x => x.PermissionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.RoleId, x.AppPageId, x.PermissionId })
                    .IsUnique();
            });

            // =========================
            // UserPagePermission
            // =========================
            builder.Entity<UserPagePermission>(entity =>
            {
                entity.ToTable("UserPagePermissions");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.AppPage)
                    .WithMany()
                    .HasForeignKey(x => x.AppPageId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasOne(x => x.Permission)
                    .WithMany()
                    .HasForeignKey(x => x.PermissionId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.UserId, x.AppPageId, x.PermissionId })
                    .IsUnique();
            });

            // =========================
            // Tenant
            // =========================
            builder.Entity<Tenant>(entity =>
            {
                entity.ToTable("Tenants");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(200).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
                entity.Property(x => x.InstitutionType).HasMaxLength(100).IsRequired();
                entity.Property(x => x.OwnerName).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Email).HasMaxLength(150).IsRequired();

                entity.HasIndex(x => x.Code).IsUnique();
            });

            // =========================
            // Feature
            // =========================
            builder.Entity<Feature>(entity =>
            {
                entity.ToTable("Features");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(100).IsRequired();

                entity.HasIndex(x => x.Code).IsUnique();
            });

            // =========================
            // SubscriptionPlan
            // =========================
            builder.Entity<SubscriptionPlan>(entity =>
            {
                entity.ToTable("SubscriptionPlans");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Code).HasMaxLength(50).IsRequired();
                entity.Property(x => x.BillingType).HasMaxLength(50).IsRequired();

                entity.HasIndex(x => x.Code).IsUnique();
            });

            // =========================
            // PlanFeature
            // =========================
            builder.Entity<PlanFeature>(entity =>
            {
                entity.ToTable("PlanFeatures");

                entity.HasKey(x => x.Id);

                entity.HasIndex(x => new { x.SubscriptionPlanId, x.FeatureId }).IsUnique();
            });

            // =========================
            // TenantSubscription
            // =========================
            builder.Entity<TenantSubscription>(entity =>
            {
                entity.ToTable("TenantSubscriptions");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.SubscriptionPlan)
                    .WithMany()
                    .HasForeignKey(x => x.SubscriptionPlanId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            // =========================
            // TenantFeature
            // =========================
            builder.Entity<TenantFeature>(entity =>
            {
                entity.ToTable("TenantFeatures");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.Feature)
                    .WithMany()
                    .HasForeignKey(x => x.FeatureId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.TenantId, x.FeatureId }).IsUnique();
            });

            // =========================
            // UsageSnapshot
            // =========================
            builder.Entity<UsageSnapshot>(entity =>
            {
                entity.ToTable("UsageSnapshots");

                entity.HasKey(x => x.Id);
            });

            // =========================
            // BillingInvoice
            // =========================
            builder.Entity<BillingInvoice>(entity =>
            {
                entity.ToTable("BillingInvoices");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.InvoiceNo).HasMaxLength(100).IsRequired();
                entity.Property(x => x.Status).HasMaxLength(50).IsRequired();

                entity.HasIndex(x => x.InvoiceNo).IsUnique();
            });

            // =========================
            // EmailVerificationToken
            // =========================
            builder.Entity<EmailVerificationToken>(entity =>
            {
                entity.ToTable("EmailVerificationTokens");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Email).HasMaxLength(150).IsRequired();
                entity.Property(x => x.Token).HasMaxLength(200).IsRequired();
            });

            // =========================
            // OnboardingProgress
            // =========================
            builder.Entity<OnboardingProgress>(entity =>
            {
                entity.ToTable("OnboardingProgresses");

                entity.HasKey(x => x.Id);

                entity.HasIndex(x => x.TenantId).IsUnique();
            });

            builder.Entity<TenantUser>(entity =>
            {
                entity.ToTable("TenantUsers");

                entity.HasKey(x => x.Id);

                entity.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                entity.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
            });
        }
    }
}