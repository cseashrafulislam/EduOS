using EduOS.Core.Common;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Employees;
using EduOS.Core.Entities.Exams;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Tenants;
using EduOS.Core.Entities.Transport;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;

namespace EduOS.Persistence.Context
{
    public class EduOSDbContext : DbContext
    {
        private readonly ICurrentUserService? _currentUser;

        public EduOSDbContext(DbContextOptions<EduOSDbContext> options, ICurrentUserService? currentUser = null) 
            : base(options)
        {
            _currentUser = currentUser;
        }

        // Auth
        public DbSet<User> Users => Set<User>();
        public DbSet<Role> Roles => Set<Role>();
        public DbSet<Permission> Permissions => Set<Permission>();
        public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
        public DbSet<UserRole> UserRoles => Set<UserRole>();
        public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
        public DbSet<TwoFactorAuth> TwoFactorAuths => Set<TwoFactorAuth>();

        // Tenants
        public DbSet<Tenant> Tenants => Set<Tenant>();
        public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
        public DbSet<Campus> Campuses => Set<Campus>();
        public DbSet<Shift> Shifts => Set<Shift>();
        public DbSet<Medium> Mediums => Set<Medium>();

        // SaaS
        public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
        public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
        public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
        public DbSet<Feature> Features => Set<Feature>();
        public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
        public DbSet<TrialAccount> TrialAccounts => Set<TrialAccount>();
        public DbSet<UsageStatistics> UsageStatistics => Set<UsageStatistics>();

        // Academic
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
        public DbSet<Designation> Designations => Set<Designation>();
        public DbSet<Employee> Employees => Set<Employee>();

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
        public DbSet<Result> Results => Set<Result>();
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

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Apply all configurations from assembly
            modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

            // Apply soft delete query filter for all entities inheriting BaseEntity
            ApplyGlobalFilters(modelBuilder);

            base.OnModelCreating(modelBuilder);
        }

        private void ApplyGlobalFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                // Soft delete filter
                if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
                {
                    var parameter = Expression.Parameter(entityType.ClrType, "e");
                    var isDeletedProperty = Expression.Property(parameter, nameof(BaseEntity.IsDeleted));
                    var notDeleted = Expression.Not(isDeletedProperty);
                    var lambda = Expression.Lambda(notDeleted, parameter);
                    modelBuilder.Entity(entityType.ClrType).HasQueryFilter(lambda);
                }
            }
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            // Auto-set audit fields
            var entries = ChangeTracker.Entries<BaseEntity>();

            foreach (var entry in entries)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAt = DateTime.UtcNow;
                        if (_currentUser?.IsAuthenticated == true)
                            entry.Entity.CreatedBy = _currentUser.UserId;

                        // Auto-set TenantId for tenant entities
                        if (entry.Entity is BaseTenantEntity tenantEntity 
                            && tenantEntity.TenantId == 0 
                            && _currentUser?.IsAuthenticated == true)
                        {
                            tenantEntity.TenantId = _currentUser.TenantId;
                        }
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        if (_currentUser?.IsAuthenticated == true)
                            entry.Entity.UpdatedBy = _currentUser.UserId;
                        break;

                    case EntityState.Deleted:
                        // Soft delete instead of hard delete
                        entry.State = EntityState.Modified;
                        entry.Entity.IsDeleted = true;
                        entry.Entity.UpdatedAt = DateTime.UtcNow;
                        if (_currentUser?.IsAuthenticated == true)
                            entry.Entity.UpdatedBy = _currentUser.UserId;
                        break;
                }
            }

            return await base.SaveChangesAsync(cancellationToken);
        }
    }
}
