using EduOS.Core.Common;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

namespace EduOS.Persistence.Context
{
    public class EduOSDbContext :
        IdentityDbContext<
            ApplicationUser,
            ApplicationRole,
            long,
            IdentityUserClaim<long>,
            IdentityUserRole<long>,
            IdentityUserLogin<long>,
            IdentityRoleClaim<long>,
            IdentityUserToken<long>>,
        IUnitOfWork
    {
        #region Private Fields

        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly DateTime _startTime = DateTime.UtcNow;
        private IDbContextTransaction? _transaction;
        private bool _isAuditing;

        // Lazily resolved user metadata. Tenant context stays request-dynamic because
        // middleware may resolve it after the DbContext has been constructed.
        private long? _userId;
        private string? _userName;
        private bool _contextResolved;

        #endregion

        #region Single Constructor

        public EduOSDbContext(
            DbContextOptions<EduOSDbContext> options,
            IHttpContextAccessor? httpContextAccessor = null)
            : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        #endregion

        #region Claim Resolution (no UserManager — no circular dependency)

        private void ResolveContext()
        {
            if (_contextResolved) return;
            _contextResolved = true;

            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true) return;

            // UserId from NameIdentifier claim
            var uidStr = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (long.TryParse(uidStr, out var uid)) _userId = uid;

            // FullName from custom claim
            _userName = user.FindFirstValue("FullName")
                        ?? user.Identity.Name
                        ?? "System";
        }

        // Tenant resolution accepts the canonical cookie claim, JWT claim variants,
        // and the trusted value populated by TenantContextMiddleware.
        private long? TenantId
        {
            get
            {
                var httpContext = _httpContextAccessor?.HttpContext;

                if (httpContext?.Items.TryGetValue("TenantId", out var itemValue) == true)
                {
                    if (itemValue is long itemTenantId && itemTenantId > 0)
                        return itemTenantId;

                    if (long.TryParse(itemValue?.ToString(), out var parsedItemTenantId)
                        && parsedItemTenantId > 0)
                        return parsedItemTenantId;
                }

                var user = httpContext?.User;
                if (user?.Identity?.IsAuthenticated != true)
                    return null;

                var claimValue = user.FindFirstValue("TenantId")
                                 ?? user.FindFirstValue("tenantId")
                                 ?? user.FindFirstValue("tenant_id");

                return long.TryParse(claimValue, out var claimTenantId) && claimTenantId > 0
                    ? claimTenantId
                    : null;
            }
        }

        /// <summary>
        /// Used by EF Core's parameterized global query filters. Zero deliberately
        /// matches no valid tenant when a request has no tenant context.
        /// </summary>
        public long CurrentTenantId => TenantId ?? 0;

        private long? UserId { get { ResolveContext(); return _userId; } }
        private string UserName { get { ResolveContext(); return _userName ?? "System"; } }

        private string IpAddress =>
            _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? "Unknown";
        private string UserAgent =>
            _httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"]
                .FirstOrDefault() ?? "Unknown";
        private string Endpoint =>
            _httpContextAccessor?.HttpContext?.Request?.Path.ToString() ?? "Unknown";

        #endregion

        #region DbSet Properties

        // IdentityDbContext already provides Users/Roles via base.
        // These aliases make it easier for service code to reference them.
        public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
        public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();

        // Auth (custom)
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
        public DbSet<TwoFactorAuth> TwoFactorAuths => Set<TwoFactorAuth>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        // Tenants
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<Campus> Campuses => Set<Campus>();
        public DbSet<Shift> Shifts => Set<Shift>();
        public DbSet<Medium> Mediums => Set<Medium>();
        public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

        // SaaS
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
        public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
        public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
        public DbSet<Feature> Features => Set<Feature>();
        public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
        public DbSet<TrialAccount> TrialAccounts => Set<TrialAccount>();
        public DbSet<UsageStatistics> UsageStatistics => Set<UsageStatistics>();

        // Academic
        public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
        public DbSet<AcademicTerm> AcademicTerms => Set<AcademicTerm>();

        public DbSet<Class> Classes => Set<Class>();
        public DbSet<Section> Sections => Set<Section>();
        public DbSet<Group> Groups => Set<Group>();
        public DbSet<Subject> Subjects => Set<Subject>();
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<SubjectTeacher> SubjectTeachers => Set<SubjectTeacher>();
        public DbSet<ClassRoutine> ClassRoutines => Set<ClassRoutine>();
        public DbSet<Substitution> Substitutions => Set<Substitution>();
        public DbSet<LessonPlan> LessonPlans => Set<LessonPlan>();
        public DbSet<Holiday> Holidays => Set<Holiday>();
        public DbSet<Event> Events => Set<Event>();

        // Students
        public DbSet<Admission> Admissions => Set<Admission>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Guardian> Guardians => Set<Guardian>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<TransferCertificate> TransferCertificates => Set<TransferCertificate>();
        public DbSet<HealthRecord> HealthRecords => Set<HealthRecord>();
        public DbSet<BehaviorRecord> BehaviorRecords => Set<BehaviorRecord>();

        // Employees
        public DbSet<HRDesignation> HRDesignations => Set<HRDesignation>();
        public DbSet<HREmployee> HREmployees => Set<HREmployee>();
        public DbSet<HRAttendanceLog> HRAttendanceLogs => Set<HRAttendanceLog>();

        // Attendance
        public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
        public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
        public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
        public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();

        // Exams
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<ExamSchedule> ExamSchedules => Set<ExamSchedule>();
        public DbSet<MarkEntry> MarkEntries => Set<MarkEntry>();
        public DbSet<GradeRule> GradeRules => Set<GradeRule>();
        public DbSet<ExamResult> ExamResults => Set<ExamResult>();
        public DbSet<Tabulation> Tabulations => Set<Tabulation>();
        public DbSet<ExamHall> ExamHalls => Set<ExamHall>();
        public DbSet<SeatPlan> SeatPlans => Set<SeatPlan>();
        public DbSet<AdmitCard> AdmitCards => Set<AdmitCard>();
        public DbSet<Question> Questions => Set<Question>();
        public DbSet<OnlineExam> OnlineExams => Set<OnlineExam>();
        public DbSet<OnlineExamQuestion> OnlineExamQuestions => Set<OnlineExamQuestion>();
        public DbSet<OnlineExamAttempt> OnlineExamAttempts => Set<OnlineExamAttempt>();

        // Finance
        public DbSet<FeeHead> FeeHeads => Set<FeeHead>();
        public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
        public DbSet<StudentInvoice> StudentInvoices => Set<StudentInvoice>();
        public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
        public DbSet<Payment> Payments => Set<Payment>();
        public DbSet<Discount> Discounts => Set<Discount>();
        public DbSet<StudentDiscount> StudentDiscounts => Set<StudentDiscount>();
        public DbSet<Fine> Fines => Set<Fine>();
        public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
        public DbSet<IncomeCategory> IncomeCategories => Set<IncomeCategory>();
        public DbSet<ExpenseCategory> ExpenseCategories => Set<ExpenseCategory>();
        public DbSet<Income> Incomes => Set<Income>();
        public DbSet<Expense> Expenses => Set<Expense>();
        public DbSet<Account> Accounts => Set<Account>();
        public DbSet<Voucher> Vouchers => Set<Voucher>();
        public DbSet<VoucherDetail> VoucherDetails => Set<VoucherDetail>();

        // Payroll
        public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
        public DbSet<Payroll> Payrolls => Set<Payroll>();
        public DbSet<Increment> Increments => Set<Increment>();
        public DbSet<LoanAdvance> LoanAdvances => Set<LoanAdvance>();
        public DbSet<Bonus> Bonuses => Set<Bonus>();

        // Library
        public DbSet<Book> Books => Set<Book>();
        public DbSet<BookIssue> BookIssues => Set<BookIssue>();

        // Transport
        public DbSet<Route> Routes => Set<Route>();
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<StudentTransport> StudentTransports => Set<StudentTransport>();

        // Hostel
        public DbSet<Hostel> Hostels => Set<Hostel>();
        public DbSet<HostelRoom> HostelRooms => Set<HostelRoom>();
        public DbSet<StudentHostel> StudentHostels => Set<StudentHostel>();

        // LMS
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Lesson> Lessons => Set<Lesson>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
        public DbSet<Homework> Homeworks => Set<Homework>();
        public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
        public DbSet<LiveClass> LiveClasses => Set<LiveClass>();

        // Inventory
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<AssetMaintenance> AssetMaintenances => Set<AssetMaintenance>();

        // Communication
        public DbSet<Notice> Notices => Set<Notice>();
        public DbSet<NoticeCategory> NoticeCategories => Set<NoticeCategory>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<SmsGateway> SmsGateways => Set<SmsGateway>();
        public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
        public DbSet<Message> Messages => Set<Message>();
        public DbSet<FeeReminder> FeeReminders => Set<FeeReminder>();
        public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();
        public DbSet<MessageQueue> MessageQueues => Set<MessageQueue>();
        public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

        // System
        public DbSet<Document> Documents => Set<Document>();
        public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
        public DbSet<IdCard> IdCards => Set<IdCard>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<BackupHistory> BackupHistories => Set<BackupHistory>();
        public DbSet<ScheduledJob> ScheduledJobs => Set<ScheduledJob>();
        public DbSet<Visitor> Visitors => Set<Visitor>();
        public DbSet<Complaint> Complaints => Set<Complaint>();
        public DbSet<Survey> Surveys => Set<Survey>();
        public DbSet<SurveyQuestion> SurveyQuestions => Set<SurveyQuestion>();
        public DbSet<SurveyResponse> SurveyResponses => Set<SurveyResponse>();
        public DbSet<Album> Albums => Set<Album>();
        public DbSet<AlbumPhoto> AlbumPhotos => Set<AlbumPhoto>();
        public DbSet<CustomField> CustomFields => Set<CustomField>();
        public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
        public DbSet<Dashboard> Dashboards => Set<Dashboard>();
        public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();
        public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
        public DbSet<ImportLog> ImportLogs => Set<ImportLog>();
        public DbSet<Language> Languages => Set<Language>();
        public DbSet<Currency> Currencies => Set<Currency>();

        #endregion

        #region Model Configuration

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            if (modelBuilder == null)
                throw new ArgumentNullException(nameof(modelBuilder));

            // MUST be first: sets up Identity tables (AspNetUsers, etc.)
            base.OnModelCreating(modelBuilder);

            // All IEntityTypeConfiguration<T> classes in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Global: soft delete + multi-tenancy
            ApplyGlobalFilters(modelBuilder);

            // datetime2 + default string lengths
            ApplyDateTimePrecision(modelBuilder);

            // No cascade deletes
            DisableCascadeDelete(modelBuilder);

            // AuditLog special config
            ConfigureAuditLog(modelBuilder);
        }

        private void ApplyGlobalFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var clrType = entityType.ClrType;

                // Soft delete is always applied. Tenant isolation is combined into
                // the same expression so one unnamed filter cannot overwrite another.
                if (typeof(BaseEntity).IsAssignableFrom(clrType))
                {
                    var p = Expression.Parameter(clrType, "e");
                    Expression filter = Expression.Not(
                        Expression.Property(p, nameof(BaseEntity.IsDeleted)));

                    if (typeof(ITenantScopedEntity).IsAssignableFrom(clrType))
                    {
                        var entityTenantId = Expression.Property(
                            p, nameof(ITenantScopedEntity.TenantId));
                        var currentTenantId = Expression.Property(
                            Expression.Constant(this), nameof(CurrentTenantId));

                        filter = Expression.AndAlso(
                            filter,
                            Expression.Equal(entityTenantId, currentTenantId));
                    }

                    modelBuilder.Entity(clrType)
                        .HasQueryFilter(Expression.Lambda(filter, p));
                }
            }
        }

        private static void ApplyDateTimePrecision(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                foreach (var prop in entityType.GetProperties())
                {
                    if (prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?))
                        prop.SetColumnType("datetime2");
                    else if (prop.ClrType == typeof(string) && prop.GetMaxLength() == null)
                        prop.SetMaxLength(500);
                }
        }

        private static void DisableCascadeDelete(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
                foreach (var fk in entityType.GetForeignKeys())
                    fk.DeleteBehavior = DeleteBehavior.Restrict;
        }

        private static void ConfigureAuditLog(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditLog>(e =>
            {
                e.ToTable("AuditLogs");
                e.HasKey(x => x.Id);
                e.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
                e.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
                e.HasIndex(x => new { x.TenantId, x.UserId, x.CreatedAt });
                e.HasIndex(x => new { x.TenantId, x.TableName, x.RecordId });
            });
        }

        #endregion

        #region SaveChanges with Audit Logging

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (_isAuditing)
                return await base.SaveChangesAsync(cancellationToken);

            ResolveContext(); // ensure claims resolved

            var entries = ChangeTracker.Entries<BaseEntity>().ToList();
            var now = DateTime.UtcNow;
            var auditLogs = new List<AuditLog>();

            try
            {
                ValidateTenantBoundaries(entries);

                foreach (var entry in entries)
                {
                    if (entry.Entity is AuditLog) continue;

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            SetAuditFields(entry, now, EntityState.Added);
                            var a = CreateAuditLog(entry, "Create", now);
                            if (a != null) auditLogs.Add(a);
                            break;

                        case EntityState.Modified:
                            SetAuditFields(entry, now, EntityState.Modified);
                            var m = CreateAuditLog(entry, "Update", now);
                            if (m != null) auditLogs.Add(m);
                            break;

                        case EntityState.Deleted:
                            entry.State = EntityState.Modified;
                            entry.Entity.IsDeleted = true;
                            SetAuditFields(entry, now, EntityState.Modified);
                            var d = CreateAuditLog(entry, "Delete", now);
                            if (d != null) auditLogs.Add(d);
                            break;
                    }
                }

                var result = await base.SaveChangesAsync(cancellationToken);

                if (auditLogs.Count > 0)
                {
                    _isAuditing = true;
                    try { await AddAuditLogsAsync(auditLogs, cancellationToken); }
                    finally { _isAuditing = false; }
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveChangesAsync failed: {ex.Message}");
                throw;
            }
        }

        private void SetAuditFields(EntityEntry entry, DateTime now, EntityState state)
        {
            if (entry.Entity is not BaseEntity entity) return;

            if (state == EntityState.Added)
            {
                entity.CreatedAt = now;
                entity.CreatedBy = UserId;
                if (entity is ITenantScopedEntity tenantEntity && tenantEntity.TenantId == 0)
                    tenantEntity.TenantId = TenantId
                        ?? throw new InvalidOperationException(
                            "Tenant-scoped data requires an explicit tenant context.");
            }
            else
            {
                entity.UpdatedAt = now;
                entity.UpdatedBy = UserId;
            }
        }

        private void ValidateTenantBoundaries(IEnumerable<EntityEntry<BaseEntity>> entries)
        {
            var requestTenantId = TenantId;
            var user = _httpContextAccessor?.HttpContext?.User;
            var isAuthenticated = user?.Identity?.IsAuthenticated == true;
            var isPlatformAdmin = user?.IsInRole("SuperAdmin") == true;

            foreach (var entry in entries.Where(e =>
                         e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
            {
                if (entry.Entity is not ITenantScopedEntity tenantEntity)
                    continue;

                if (requestTenantId.HasValue)
                {
                    if (entry.State == EntityState.Added && tenantEntity.TenantId == 0)
                        tenantEntity.TenantId = requestTenantId.Value;

                    if (tenantEntity.TenantId != requestTenantId.Value)
                        throw new UnauthorizedAccessException(
                            "Tenant boundary violation. The record belongs to another tenant.");

                    continue;
                }

                if (isAuthenticated && !isPlatformAdmin)
                    throw new UnauthorizedAccessException(
                        "Tenant context is required for tenant-scoped data.");

                // Platform administration and background/bootstrap work must always
                // name the target tenant explicitly; TenantId zero is never accepted.
                if (tenantEntity.TenantId <= 0)
                    throw new InvalidOperationException(
                        "Tenant-scoped data requires an explicit TenantId.");
            }
        }

        private AuditLog? CreateAuditLog(EntityEntry entry, string action, DateTime now)
        {
            try
            {
                if (entry.Entity is AuditLog) return null;

                var log = new AuditLog
                {
                    TenantId = TenantId ?? 0,
                    UserId = UserId,
                    UserName = UserName,
                    Action = action,
                    TableName = entry.Entity.GetType().Name,
                    RecordId = GetEntityId(entry),
                    IpAddress = IpAddress,
                    UserAgent = UserAgent,
                    Endpoint = Endpoint,
                    ExecutionTime = DateTime.UtcNow - _startTime,
                    IsSuccess = true,
                    CreatedAt = now
                };

                switch (action)
                {
                    case "Update":
                        var old = new Dictionary<string, object?>();
                        var @new = new Dictionary<string, object?>();
                        foreach (var p in entry.Properties
                            .Where(p => p.IsModified && !IsSensitiveField(p.Metadata.Name)))
                        { old[p.Metadata.Name] = p.OriginalValue; @new[p.Metadata.Name] = p.CurrentValue; }
                        if (old.Count > 0)
                        { log.OldValue = JsonSerializer.Serialize(old); log.NewValue = JsonSerializer.Serialize(@new); }
                        break;

                    case "Create":
                        log.NewValue = JsonSerializer.Serialize(
                            entry.Properties
                                .Where(p => !IsSensitiveField(p.Metadata.Name))
                                .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue));
                        break;

                    case "Delete":
                        log.OldValue = JsonSerializer.Serialize(
                            entry.Properties
                                .Where(p => !IsSensitiveField(p.Metadata.Name))
                                .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue));
                        break;
                }

                return log;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateAuditLog failed: {ex.Message}");
                return null;
            }
        }

        private static long? GetEntityId(EntityEntry entry)
        {
            try { var p = entry.Property(nameof(BaseEntity.Id)); return p.CurrentValue is long id ? id : null; }
            catch { return null; }
        }

        private static bool IsSensitiveField(string name) =>
            new[] { "Password", "PasswordHash", "Secret", "Token", "ApiKey",
                    "ApiSecret", "CreditCard", "BankAccount", "AccountNumber",
                    "NID", "NationalId", "BirthCert", "Passport", "RefreshToken",
                    "SettingValue", "GatewayResponse" }
            .Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase));

        private async Task AddAuditLogsAsync(List<AuditLog> logs, CancellationToken ct)
        {
            try
            {
                if (logs.Count == 0) return;
                await AuditLogs.AddRangeAsync(logs, ct);
                await base.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"AddAuditLogsAsync failed: {ex.Message}");
            }
        }

        #endregion

        #region IUnitOfWork Transactions

        public IExecutionStrategy CreateExecutionStrategy()
        {
            return Database.CreateExecutionStrategy();
        }

        public async Task BeginTransactionAsync()
        {
            if (_transaction != null)
                return;

            _transaction = await Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction == null)
                return;

            try
            {
                await _transaction.CommitAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction == null)
                return;

            try
            {
                await _transaction.RollbackAsync();
            }
            finally
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public override void Dispose()
        {
            _transaction?.Dispose();
            _transaction = null;

            base.Dispose();
        }

        public override async ValueTask DisposeAsync()
        {
            if (_transaction != null)
            {
                await _transaction.DisposeAsync();
                _transaction = null;
            }

            await base.DisposeAsync();
        }

        #endregion
    }
}
