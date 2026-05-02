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

        private readonly long? _tenantId;
        private readonly long? _userId;
        private readonly string? _userName;
        private readonly string? _ipAddress;
        private readonly string? _userAgent;
        private readonly string? _endpoint;
        private readonly DateTime _startTime;
        private IDbContextTransaction? _transaction;
        private bool _isAuditing;

        #endregion

        #region Constructor (SINGLE constructor - fixes the migration error)
        public EduOSDbContext(
            DbContextOptions<EduOSDbContext> options,
            ICurrentUserService? currentUser = null,
            IHttpContextAccessor? httpContextAccessor = null)
            : base(options)
        {
            _tenantId = currentUser?.TenantId > 0 ? currentUser.TenantId : null;
            _userId = currentUser?.UserId > 0 ? currentUser.UserId : null;
            _userName = currentUser?.FullName;
            _ipAddress = httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
            _userAgent = httpContextAccessor?.HttpContext?.Request?.Headers["User-Agent"].FirstOrDefault();
            _endpoint = httpContextAccessor?.HttpContext?.Request?.Path;
            _startTime = DateTime.UtcNow;
            _isAuditing = false;
        }

        #endregion

        #region DbSet Properties

        public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
        public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();

        // ==================== Auth (custom) ====================
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
        public DbSet<TwoFactorAuth> TwoFactorAuths => Set<TwoFactorAuth>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

        // ==================== Tenants ====================
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<Campus> Campuses => Set<Campus>();
        public DbSet<Shift> Shifts => Set<Shift>();
        public DbSet<Medium> Mediums => Set<Medium>();
        public DbSet<TenantUser> TenantUsers => Set<TenantUser>();

        // ==================== SaaS ====================
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
        public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
        public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
        public DbSet<Feature> Features => Set<Feature>();
        public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
        public DbSet<TrialAccount> TrialAccounts => Set<TrialAccount>();
        public DbSet<UsageStatistics> UsageStatistics => Set<UsageStatistics>();

        // ==================== Academic ====================
        public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
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

        // ==================== Students ====================
        public DbSet<Admission> Admissions => Set<Admission>();
        public DbSet<Student> Students => Set<Student>();
        public DbSet<Guardian> Guardians => Set<Guardian>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<Promotion> Promotions => Set<Promotion>();
        public DbSet<TransferCertificate> TransferCertificates => Set<TransferCertificate>();
        public DbSet<HealthRecord> HealthRecords => Set<HealthRecord>();
        public DbSet<BehaviorRecord> BehaviorRecords => Set<BehaviorRecord>();

        // ==================== Employees ====================
        public DbSet<HRDesignation> HRDesignations => Set<HRDesignation>();
        public DbSet<HREmployee> HREmployees => Set<HREmployee>();
        public DbSet<HRAttendanceLog> HRAttendanceLogs => Set<HRAttendanceLog>();

        // ==================== Attendance ====================
        public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
        public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
        public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
        public DbSet<LeaveApplication> LeaveApplications => Set<LeaveApplication>();

        // ==================== Exams ====================
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

        // ==================== Finance ====================
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

        // ==================== Payroll ====================
        public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
        public DbSet<Payroll> Payrolls => Set<Payroll>();
        public DbSet<Increment> Increments => Set<Increment>();
        public DbSet<LoanAdvance> LoanAdvances => Set<LoanAdvance>();
        public DbSet<Bonus> Bonuses => Set<Bonus>();

        // ==================== Library ====================
        public DbSet<Book> Books => Set<Book>();
        public DbSet<BookIssue> BookIssues => Set<BookIssue>();

        // ==================== Transport ====================
        public DbSet<Route> Routes => Set<Route>();
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<StudentTransport> StudentTransports => Set<StudentTransport>();

        // ==================== Hostel ====================
        public DbSet<Hostel> Hostels => Set<Hostel>();
        public DbSet<HostelRoom> HostelRooms => Set<HostelRoom>();
        public DbSet<StudentHostel> StudentHostels => Set<StudentHostel>();

        // ==================== LMS ====================
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<Lesson> Lessons => Set<Lesson>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
        public DbSet<Homework> Homeworks => Set<Homework>();
        public DbSet<HomeworkSubmission> HomeworkSubmissions => Set<HomeworkSubmission>();
        public DbSet<LiveClass> LiveClasses => Set<LiveClass>();

        // ==================== Inventory ====================
        public DbSet<Asset> Assets => Set<Asset>();
        public DbSet<AssetMaintenance> AssetMaintenances => Set<AssetMaintenance>();

        // ==================== Communication ====================
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

        // ==================== System ====================
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

            // ⚠️ CRITICAL: base.OnModelCreating FIRST
            // This sets up all Identity tables (AspNetUsers, AspNetRoles, etc.)
            base.OnModelCreating(modelBuilder);

            // Apply all IEntityTypeConfiguration<T> classes from this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Global filters: Soft Delete + Multi-Tenancy
            ApplyGlobalFilters(modelBuilder);

            // datetime2 precision + default string max length
            ApplyDateTimePrecision(modelBuilder);

            // No cascade deletes (safer for multi-tenant data)
            DisableCascadeDelete(modelBuilder);

            // AuditLog table extra config
            ConfigureAuditLog(modelBuilder);
        }

        private void ApplyGlobalFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                var clrType = entityType.ClrType;

                // Soft Delete: applies to all BaseEntity subclasses
                if (typeof(BaseEntity).IsAssignableFrom(clrType))
                {
                    var parameter = Expression.Parameter(clrType, "e");
                    var isDeletedProp = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                    var notDeleted = Expression.Not(isDeletedProp);
                    modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(notDeleted, parameter));
                }

                // Multi-Tenancy: applies to BaseTenantEntity subclasses
                // Only adds filter if there is an active tenant context
                if (typeof(BaseTenantEntity).IsAssignableFrom(clrType) && _tenantId.HasValue)
                {
                    var parameter = Expression.Parameter(clrType, "e");
                    var tenantProp = Expression.Property(parameter, nameof(BaseTenantEntity.TenantId));
                    var tenantConst = Expression.Constant(_tenantId.Value, typeof(long));
                    var tenantFilter = Expression.Equal(tenantProp, tenantConst);
                    modelBuilder.Entity(clrType).HasQueryFilter(Expression.Lambda(tenantFilter, parameter));
                }
            }
        }

        private static void ApplyDateTimePrecision(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var property in entityType.GetProperties())
                {
                    if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    {
                        property.SetColumnType("datetime2");
                    }
                    else if (property.ClrType == typeof(string) && property.GetMaxLength() == null)
                    {
                        property.SetMaxLength(500);
                    }
                }
            }
        }

        private static void DisableCascadeDelete(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                foreach (var foreignKey in entityType.GetForeignKeys())
                {
                    foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
                }
            }
        }

        private static void ConfigureAuditLog(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AuditLog>(entity =>
            {
                entity.ToTable("AuditLogs");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.OldValue).HasColumnType("nvarchar(max)");
                entity.Property(e => e.NewValue).HasColumnType("nvarchar(max)");
                entity.HasIndex(e => new { e.TenantId, e.UserId, e.CreatedAt });
                entity.HasIndex(e => new { e.TenantId, e.TableName, e.RecordId });
            });
        }

        #endregion

        #region SaveChanges with Audit Logging

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Prevent re-entry during audit log saving
            if (_isAuditing)
                return await base.SaveChangesAsync(cancellationToken);

            var entries = ChangeTracker.Entries<BaseEntity>().ToList();
            var now = DateTime.UtcNow;
            var auditLogs = new List<AuditLog>();

            try
            {
                foreach (var entry in entries)
                {
                    // Skip AuditLog itself to prevent infinite loop
                    if (entry.Entity is AuditLog) continue;

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            SetAuditFields(entry, now, EntityState.Added);
                            var createLog = CreateAuditLog(entry, "Create");
                            if (createLog != null) auditLogs.Add(createLog);
                            break;

                        case EntityState.Modified:
                            SetAuditFields(entry, now, EntityState.Modified);
                            var updateLog = CreateAuditLog(entry, "Update");
                            if (updateLog != null) auditLogs.Add(updateLog);
                            break;

                        case EntityState.Deleted:
                            // Convert hard delete → soft delete
                            entry.State = EntityState.Modified;
                            entry.Entity.IsDeleted = true;
                            SetAuditFields(entry, now, EntityState.Modified);
                            var deleteLog = CreateAuditLog(entry, "Delete");
                            if (deleteLog != null) auditLogs.Add(deleteLog);
                            break;
                    }
                }

                // Save main changes
                var result = await base.SaveChangesAsync(cancellationToken);

                // Save audit logs (failure here doesn't break the main save)
                if (auditLogs.Count > 0)
                {
                    _isAuditing = true;
                    try
                    {
                        await AddAuditLogsAsync(auditLogs, cancellationToken);
                    }
                    finally
                    {
                        _isAuditing = false;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveChangesAsync Failed: {ex.Message}");
                throw;
            }
        }

        private void SetAuditFields(EntityEntry entry, DateTime now, EntityState state)
        {
            if (entry.Entity is not BaseEntity baseEntity) return;

            if (state == EntityState.Added)
            {
                baseEntity.CreatedAt = now;
                baseEntity.CreatedBy = _userId;

                // Auto-assign TenantId if entity is tenant-scoped and not already set
                if (baseEntity is BaseTenantEntity tenantEntity && tenantEntity.TenantId == 0)
                {
                    tenantEntity.TenantId = _tenantId ?? 0;
                }
            }
            else
            {
                baseEntity.UpdatedAt = now;
                baseEntity.UpdatedBy = _userId;
            }
        }

        private AuditLog? CreateAuditLog(EntityEntry entry, string action)
        {
            try
            {
                if (entry.Entity is AuditLog) return null;

                var auditLog = new AuditLog
                {
                    TenantId = _tenantId ?? 0,
                    UserId = _userId,
                    UserName = _userName ?? "System",
                    Action = action,
                    TableName = entry.Entity.GetType().Name,
                    RecordId = GetEntityId(entry),
                    IpAddress = _ipAddress ?? "Unknown",
                    UserAgent = _userAgent ?? "Unknown",
                    Endpoint = _endpoint ?? "Unknown",
                    ExecutionTime = DateTime.UtcNow - _startTime,
                    IsSuccess = true,
                    CreatedAt = DateTime.UtcNow
                };

                // Capture changed values, excluding sensitive fields
                if (action == "Update")
                {
                    var old = new Dictionary<string, object?>();
                    var @new = new Dictionary<string, object?>();

                    foreach (var prop in entry.Properties.Where(p =>
                        p.IsModified && !IsSensitiveField(p.Metadata.Name)))
                    {
                        old[prop.Metadata.Name] = prop.OriginalValue;
                        @new[prop.Metadata.Name] = prop.CurrentValue;
                    }

                    if (old.Count > 0)
                    {
                        auditLog.OldValue = JsonSerializer.Serialize(old);
                        auditLog.NewValue = JsonSerializer.Serialize(@new);
                    }
                }
                else if (action == "Create")
                {
                    var values = entry.Properties
                        .Where(p => !IsSensitiveField(p.Metadata.Name))
                        .ToDictionary(p => p.Metadata.Name, p => p.CurrentValue);

                    auditLog.NewValue = JsonSerializer.Serialize(values);
                }
                else if (action == "Delete")
                {
                    var values = entry.Properties
                        .Where(p => !IsSensitiveField(p.Metadata.Name))
                        .ToDictionary(p => p.Metadata.Name, p => p.OriginalValue);

                    auditLog.OldValue = JsonSerializer.Serialize(values);
                }

                return auditLog;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CreateAuditLog Failed: {ex.Message}");
                return null;
            }
        }

        private static long? GetEntityId(EntityEntry entry)
        {
            try
            {
                var idProp = entry.Property(nameof(BaseEntity.Id));
                return idProp.CurrentValue is long id ? id : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSensitiveField(string fieldName)
        {
            var sensitiveFields = new[]
            {
                "Password", "PasswordHash", "SecretKey", "Token", "ApiKey",
                "CreditCard", "BankAccount", "NID", "Passport", "RefreshToken"
            };
            return sensitiveFields.Any(f => fieldName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        private async Task AddAuditLogsAsync(List<AuditLog> auditLogs, CancellationToken cancellationToken)
        {
            try
            {
                if (auditLogs.Count == 0) return;
                await AuditLogs.AddRangeAsync(auditLogs, cancellationToken);
                await base.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Never throw from here — audit failure must not break main operation
                System.Diagnostics.Debug.WriteLine($"AddAuditLogsAsync Failed: {ex.Message}");
            }
        }

        #endregion

        #region IUnitOfWork Transactions

        public async Task BeginTransactionAsync()
        {
            _transaction ??= await Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction == null) return;
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
            if (_transaction == null) return;
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
            base.Dispose();
        }

        #endregion
    }
}