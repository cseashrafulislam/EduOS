using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Admission;
using EduOS.Core.Entities.Assessment;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Entities.Auth;
using EduOS.Core.Entities.Base;
using EduOS.Core.Entities.Communication;
using EduOS.Core.Entities.Files;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Entities.HR;
using EduOS.Core.Entities.Hostel;
using EduOS.Core.Entities.Inventory;
using EduOS.Core.Entities.Learners;
using EduOS.Core.Entities.Library;
using EduOS.Core.Entities.LMS;
using EduOS.Core.Entities.Payroll;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Entities.Transport;
using EduOS.Core.Interfaces.IRepositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;

namespace EduOS.Persistence.Context;

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
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private IDbContextTransaction? _transaction;
    private bool _savingAudit;
    private long? _userId;
    private string? _userName;
    private bool _contextResolved;
    private long? _systemTenantId;
    private LearnerConsentWriteScope? _learnerConsentWriteScope;

    private sealed record LearnerConsentWriteScope(
        long ConsentRequestId,
        long TenantId,
        long PersonId,
        long StudentId,
        long UserId);

    private sealed record PendingAudit(EntityEntry Entry, string Action, AuditLog Log);

    private sealed class TenantScope : IDisposable
    {
        private readonly EduOSDbContext _context;
        private readonly long? _previousTenantId;
        private bool _disposed;

        public TenantScope(EduOSDbContext context, long tenantId)
        {
            _context = context;
            _previousTenantId = context._systemTenantId;
            context._systemTenantId = tenantId;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _context._systemTenantId = _previousTenantId;
            _disposed = true;
        }
    }

    public EduOSDbContext(
        DbContextOptions<EduOSDbContext> options,
        IHttpContextAccessor? httpContextAccessor = null)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private void ResolveContext()
    {
        if (_contextResolved) return;
        _contextResolved = true;

        var user = _httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;

        var userIdText = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (long.TryParse(userIdText, out var userId))
            _userId = userId;

        _userName = user.FindFirstValue("FullName")
            ?? user.Identity.Name
            ?? "System";
    }

    private long? TenantId
    {
        get
        {
            var httpContext = _httpContextAccessor?.HttpContext;
            if (httpContext?.Items.TryGetValue("TenantId", out var value) == true
                && value is long tenantId
                && tenantId > 0)
            {
                return tenantId;
            }

            return _systemTenantId is > 0 ? _systemTenantId : null;
        }
    }

    public long CurrentTenantId => TenantId ?? 0;
    private long? UserId { get { ResolveContext(); return _userId; } }
    private string UserName { get { ResolveContext(); return _userName ?? "System"; } }
    private string IpAddress => _httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString() ?? "System";
    private string Endpoint => _httpContextAccessor?.HttpContext?.Request.Path.ToString() ?? "System";
    private string? RequestId => _httpContextAccessor?.HttpContext?.TraceIdentifier;
    private string? CorrelationId => Activity.Current?.TraceId.ToString();

    /// <summary>
    /// Explicit tenant scope for trusted background/bootstrap/platform work where there is no request tenant.
    /// Never use this to switch a normal authenticated tenant user to another tenant.
    /// </summary>
    public IDisposable BeginSystemTenantScope(long tenantId)
    {
        if (tenantId <= 0)
            throw new ArgumentOutOfRangeException(nameof(tenantId));

        var httpContext = _httpContextAccessor?.HttpContext;
        var user = httpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated == true;
        var isPlatformAdmin = user?.IsInRole("SuperAdmin") == true;
        var requestTenantId = httpContext?.Items.TryGetValue("TenantId", out var value) == true && value is long id && id > 0
            ? id
            : (long?)null;

        if (isAuthenticated && !isPlatformAdmin)
            throw new UnauthorizedAccessException("Only trusted system/platform work may establish an explicit tenant scope.");

        if (requestTenantId.HasValue && requestTenantId.Value != tenantId)
            throw new UnauthorizedAccessException("The requested tenant scope conflicts with the trusted request tenant.");

        if (_systemTenantId.HasValue && _systemTenantId.Value != tenantId)
            throw new InvalidOperationException("A DbContext instance cannot switch between different system tenants.");

        var trackedOtherTenant = ChangeTracker.Entries()
            .Select(x => x.Entity)
            .OfType<ITenantScopedEntity>()
            .Any(x => x.TenantId > 0 && x.TenantId != tenantId);

        if (trackedOtherTenant)
            throw new InvalidOperationException("This DbContext already tracks data from another tenant. Create a new DbContext instance.");

        return new TenantScope(this, tenantId);
    }


    #region DbSets

    // Auth / Identity
    public DbSet<ApplicationRole> ApplicationRoles => Set<ApplicationRole>();
    public DbSet<ApplicationUser> ApplicationUsers => Set<ApplicationUser>();
    public DbSet<LoginHistory> LoginHistories => Set<LoginHistory>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<TenantInvitation> TenantInvitations => Set<TenantInvitation>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<TwoFactorAuth> TwoFactorAuths => Set<TwoFactorAuth>();
    public DbSet<TwoFactorRecoveryCode> TwoFactorRecoveryCodes => Set<TwoFactorRecoveryCode>();
    public DbSet<UserCampusAccess> UserCampusAccesses => Set<UserCampusAccess>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();

    // SaaS / Tenant
    public DbSet<Campus> Campuses => Set<Campus>();
    public DbSet<Feature> Features => Set<Feature>();
    public DbSet<InstitutionTypeDefinition> InstitutionTypeDefinitions => Set<InstitutionTypeDefinition>();
    public DbSet<InstitutionTypeModule> InstitutionTypeModules => Set<InstitutionTypeModule>();
    public DbSet<LegalDocument> LegalDocuments => Set<LegalDocument>();
    public DbSet<PlanFeature> PlanFeatures => Set<PlanFeature>();
    public DbSet<ProductModule> ProductModules => Set<ProductModule>();
    public DbSet<ProductModuleFeature> ProductModuleFeatures => Set<ProductModuleFeature>();
    public DbSet<SubscriptionInvoice> SubscriptionInvoices => Set<SubscriptionInvoice>();
    public DbSet<SubscriptionInvoiceLine> SubscriptionInvoiceLines => Set<SubscriptionInvoiceLine>();
    public DbSet<SubscriptionPayment> SubscriptionPayments => Set<SubscriptionPayment>();
    public DbSet<SubscriptionPlan> SubscriptionPlans => Set<SubscriptionPlan>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantDomain> TenantDomains => Set<TenantDomain>();
    public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();
    public DbSet<TenantModule> TenantModules => Set<TenantModule>();
    public DbSet<TenantSetting> TenantSettings => Set<TenantSetting>();
    public DbSet<TenantSubscription> TenantSubscriptions => Set<TenantSubscription>();
    public DbSet<TenantTerminology> TenantTerminologies => Set<TenantTerminology>();
    public DbSet<UsageStatistics> UsageStatistics => Set<UsageStatistics>();
    public DbSet<UserLegalAcceptance> UserLegalAcceptances => Set<UserLegalAcceptance>();

    // Global Learner Identity
    public DbSet<LearnerConsentRequest> LearnerConsentRequests => Set<LearnerConsentRequest>();
    public DbSet<LearnerDataGrant> LearnerDataGrants => Set<LearnerDataGrant>();
    public DbSet<LearnerIdentityAccessLog> LearnerIdentityAccessLogs => Set<LearnerIdentityAccessLog>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<PersonAddress> PersonAddresses => Set<PersonAddress>();
    public DbSet<PersonIdentifier> PersonIdentifiers => Set<PersonIdentifier>();
    public DbSet<PersonMergeRecord> PersonMergeRecords => Set<PersonMergeRecord>();
    public DbSet<StudentPersonLink> StudentPersonLinks => Set<StudentPersonLink>();

    // Students
    public DbSet<Guardian> Guardians => Set<Guardian>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<StudentExitRecord> StudentExitRecords => Set<StudentExitRecord>();
    public DbSet<StudentGuardian> StudentGuardians => Set<StudentGuardian>();
    public DbSet<StudentPromotionRecord> StudentPromotionRecords => Set<StudentPromotionRecord>();
    public DbSet<StudentStatusHistory> StudentStatusHistories => Set<StudentStatusHistory>();
    public DbSet<TransferCertificate> TransferCertificates => Set<TransferCertificate>();
    public DbSet<TransferRequest> TransferRequests => Set<TransferRequest>();

    // Academic
    public DbSet<AcademicBatch> AcademicBatches => Set<AcademicBatch>();
    public DbSet<AcademicCalendarEvent> AcademicCalendarEvents => Set<AcademicCalendarEvent>();
    public DbSet<AcademicCalendarPolicy> AcademicCalendarPolicies => Set<AcademicCalendarPolicy>();
    public DbSet<AcademicCurriculum> AcademicCurriculums => Set<AcademicCurriculum>();
    public DbSet<AcademicDepartment> AcademicDepartments => Set<AcademicDepartment>();
    public DbSet<AcademicLevel> AcademicLevels => Set<AcademicLevel>();
    public DbSet<AcademicProgram> AcademicPrograms => Set<AcademicProgram>();
    public DbSet<AcademicTerm> AcademicTerms => Set<AcademicTerm>();
    public DbSet<AcademicTrack> AcademicTracks => Set<AcademicTrack>();
    public DbSet<AcademicYear> AcademicYears => Set<AcademicYear>();
    public DbSet<AttendanceSession> AttendanceSessions => Set<AttendanceSession>();
    public DbSet<CurriculumSubject> CurriculumSubjects => Set<CurriculumSubject>();
    public DbSet<InstructorAssignment> InstructorAssignments => Set<InstructorAssignment>();
    public DbSet<LessonPlan> LessonPlans => Set<LessonPlan>();
    public DbSet<Medium> Mediums => Set<Medium>();
    public DbSet<ProgramCampus> ProgramCampuses => Set<ProgramCampus>();
    public DbSet<Room> Rooms => Set<Room>();
    public DbSet<RoutineEntry> RoutineEntries => Set<RoutineEntry>();
    public DbSet<RoutineTimeSlot> RoutineTimeSlots => Set<RoutineTimeSlot>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<StudentSubjectRegistration> StudentSubjectRegistrations => Set<StudentSubjectRegistration>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<SubjectOffering> SubjectOfferings => Set<SubjectOffering>();
    public DbSet<SubjectPrerequisite> SubjectPrerequisites => Set<SubjectPrerequisite>();
    public DbSet<Substitution> Substitutions => Set<Substitution>();

    // Admission
    public DbSet<AdmissionApplicant> AdmissionApplicants => Set<AdmissionApplicant>();
    public DbSet<AdmissionApplicantDocument> AdmissionApplicantDocuments => Set<AdmissionApplicantDocument>();
    public DbSet<AdmissionApplicantFieldValue> AdmissionApplicantFieldValues => Set<AdmissionApplicantFieldValue>();
    public DbSet<AdmissionApplicantGuardian> AdmissionApplicantGuardians => Set<AdmissionApplicantGuardian>();
    public DbSet<AdmissionDecision> AdmissionDecisions => Set<AdmissionDecision>();
    public DbSet<AdmissionFormField> AdmissionFormFields => Set<AdmissionFormField>();
    public DbSet<AdmissionIntakeForm> AdmissionIntakeForms => Set<AdmissionIntakeForm>();
    public DbSet<AdmissionPayment> AdmissionPayments => Set<AdmissionPayment>();
    public DbSet<AdmissionResult> AdmissionResults => Set<AdmissionResult>();
    public DbSet<AdmissionTest> AdmissionTests => Set<AdmissionTest>();

    // Assessment / Result
    public DbSet<Assessment> Assessments => Set<Assessment>();
    public DbSet<AssessmentComponent> AssessmentComponents => Set<AssessmentComponent>();
    public DbSet<AssessmentSchedule> AssessmentSchedules => Set<AssessmentSchedule>();
    public DbSet<AssessmentSubject> AssessmentSubjects => Set<AssessmentSubject>();
    public DbSet<CertificateIssue> CertificateIssues => Set<CertificateIssue>();
    public DbSet<CertificateTemplate> CertificateTemplates => Set<CertificateTemplate>();
    public DbSet<GradeRule> GradeRules => Set<GradeRule>();
    public DbSet<GradeScheme> GradeSchemes => Set<GradeScheme>();
    public DbSet<ResultPublication> ResultPublications => Set<ResultPublication>();
    public DbSet<StudentAssessmentComponentMark> StudentAssessmentComponentMarks => Set<StudentAssessmentComponentMark>();
    public DbSet<StudentAssessmentMark> StudentAssessmentMarks => Set<StudentAssessmentMark>();
    public DbSet<StudentResultSummary> StudentResultSummaries => Set<StudentResultSummary>();
    public DbSet<TranscriptIssue> TranscriptIssues => Set<TranscriptIssue>();

    // Attendance / Leave
    public DbSet<EmployeeAttendance> EmployeeAttendances => Set<EmployeeAttendance>();
    public DbSet<EmployeeAttendanceAdjustment> EmployeeAttendanceAdjustments => Set<EmployeeAttendanceAdjustment>();
    public DbSet<EmployeeLeaveAdjustment> EmployeeLeaveAdjustments => Set<EmployeeLeaveAdjustment>();
    public DbSet<EmployeeLeaveApplication> EmployeeLeaveApplications => Set<EmployeeLeaveApplication>();
    public DbSet<EmployeeLeaveEntitlement> EmployeeLeaveEntitlements => Set<EmployeeLeaveEntitlement>();
    public DbSet<LeaveType> LeaveTypes => Set<LeaveType>();
    public DbSet<StudentAttendance> StudentAttendances => Set<StudentAttendance>();
    public DbSet<StudentAttendanceAdjustment> StudentAttendanceAdjustments => Set<StudentAttendanceAdjustment>();
    public DbSet<StudentLeaveApplication> StudentLeaveApplications => Set<StudentLeaveApplication>();

    // HR
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<EmployeeAssignmentHistory> EmployeeAssignmentHistories => Set<EmployeeAssignmentHistory>();
    public DbSet<EmployeeBankAccount> EmployeeBankAccounts => Set<EmployeeBankAccount>();
    public DbSet<EmployeeCampusAssignment> EmployeeCampusAssignments => Set<EmployeeCampusAssignment>();
    public DbSet<EmployeeShiftAssignment> EmployeeShiftAssignments => Set<EmployeeShiftAssignment>();
    public DbSet<OrganizationUnit> OrganizationUnits => Set<OrganizationUnit>();
    public DbSet<WorkShift> WorkShifts => Set<WorkShift>();

    // Student Finance
    public DbSet<DiscountRule> DiscountRules => Set<DiscountRule>();
    public DbSet<FeeHead> FeeHeads => Set<FeeHead>();
    public DbSet<FeeStructure> FeeStructures => Set<FeeStructure>();
    public DbSet<FeeStructureLine> FeeStructureLines => Set<FeeStructureLine>();
    public DbSet<FineRule> FineRules => Set<FineRule>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<RefundAllocation> RefundAllocations => Set<RefundAllocation>();
    public DbSet<StudentDiscount> StudentDiscounts => Set<StudentDiscount>();
    public DbSet<StudentFine> StudentFines => Set<StudentFine>();
    public DbSet<StudentInvoice> StudentInvoices => Set<StudentInvoice>();
    public DbSet<StudentInvoiceLine> StudentInvoiceLines => Set<StudentInvoiceLine>();
    public DbSet<StudentPayment> StudentPayments => Set<StudentPayment>();

    // Accounting
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<AccountingPeriod> AccountingPeriods => Set<AccountingPeriod>();
    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();
    public DbSet<Journal> Journals => Set<Journal>();
    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    // Payroll
    public DbSet<Bonus> Bonuses => Set<Bonus>();
    public DbSet<LoanAdvance> LoanAdvances => Set<LoanAdvance>();
    public DbSet<LoanAdvanceRecovery> LoanAdvanceRecoveries => Set<LoanAdvanceRecovery>();
    public DbSet<PayrollEmployee> PayrollEmployees => Set<PayrollEmployee>();
    public DbSet<PayrollLine> PayrollLines => Set<PayrollLine>();
    public DbSet<PayrollPayment> PayrollPayments => Set<PayrollPayment>();
    public DbSet<PayrollRun> PayrollRuns => Set<PayrollRun>();
    public DbSet<SalaryComponent> SalaryComponents => Set<SalaryComponent>();
    public DbSet<SalaryStructure> SalaryStructures => Set<SalaryStructure>();
    public DbSet<SalaryStructureLine> SalaryStructureLines => Set<SalaryStructureLine>();

    // Library
    public DbSet<Book> Books => Set<Book>();
    public DbSet<BookCategory> BookCategories => Set<BookCategory>();
    public DbSet<BookCopy> BookCopies => Set<BookCopy>();
    public DbSet<BookIssue> BookIssues => Set<BookIssue>();
    public DbSet<BookReservation> BookReservations => Set<BookReservation>();
    public DbSet<LibraryBranch> LibraryBranches => Set<LibraryBranch>();

    // Transport
    public DbSet<Route> Routes => Set<Route>();
    public DbSet<RouteStop> RouteStops => Set<RouteStop>();
    public DbSet<RouteVehicleAssignment> RouteVehicleAssignments => Set<RouteVehicleAssignment>();
    public DbSet<StudentTransport> StudentTransports => Set<StudentTransport>();
    public DbSet<TransportDriver> TransportDrivers => Set<TransportDriver>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<VehicleDriverAssignment> VehicleDriverAssignments => Set<VehicleDriverAssignment>();

    // Hostel
    public DbSet<Hostel> Hostels => Set<Hostel>();
    public DbSet<HostelBed> HostelBeds => Set<HostelBed>();
    public DbSet<HostelRoom> HostelRooms => Set<HostelRoom>();
    public DbSet<StudentHostelAllocation> StudentHostelAllocations => Set<StudentHostelAllocation>();

    // LMS
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<AssignmentAttachment> AssignmentAttachments => Set<AssignmentAttachment>();
    public DbSet<AssignmentSubmission> AssignmentSubmissions => Set<AssignmentSubmission>();
    public DbSet<AssignmentSubmissionFile> AssignmentSubmissionFiles => Set<AssignmentSubmissionFile>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseEnrollment> CourseEnrollments => Set<CourseEnrollment>();
    public DbSet<CourseSection> CourseSections => Set<CourseSection>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonProgress> LessonProgresses => Set<LessonProgress>();
    public DbSet<LessonResource> LessonResources => Set<LessonResource>();
    public DbSet<LiveClass> LiveClasses => Set<LiveClass>();
    public DbSet<Quiz> Quizzes => Set<Quiz>();
    public DbSet<QuizAnswer> QuizAnswers => Set<QuizAnswer>();
    public DbSet<QuizAnswerOption> QuizAnswerOptions => Set<QuizAnswerOption>();
    public DbSet<QuizAttempt> QuizAttempts => Set<QuizAttempt>();
    public DbSet<QuizOption> QuizOptions => Set<QuizOption>();
    public DbSet<QuizQuestion> QuizQuestions => Set<QuizQuestion>();

    // Inventory / Assets
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    public DbSet<AssetCategory> AssetCategories => Set<AssetCategory>();
    public DbSet<AssetMaintenance> AssetMaintenances => Set<AssetMaintenance>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<InventoryLocation> InventoryLocations => Set<InventoryLocation>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementLine> InventoryMovementLines => Set<InventoryMovementLine>();

    // Communication
    public DbSet<CommunicationDelivery> CommunicationDeliveries => Set<CommunicationDelivery>();
    public DbSet<CommunicationGateway> CommunicationGateways => Set<CommunicationGateway>();
    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageAttachment> MessageAttachments => Set<MessageAttachment>();
    public DbSet<MessageTemplate> MessageTemplates => Set<MessageTemplate>();
    public DbSet<MessageThread> MessageThreads => Set<MessageThread>();
    public DbSet<MessageThreadParticipant> MessageThreadParticipants => Set<MessageThreadParticipant>();
    public DbSet<Notice> Notices => Set<Notice>();
    public DbSet<NoticeAudience> NoticeAudiences => Set<NoticeAudience>();
    public DbSet<NoticeCategory> NoticeCategories => Set<NoticeCategory>();
    public DbSet<NoticeReadReceipt> NoticeReadReceipts => Set<NoticeReadReceipt>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<NotificationPreference> NotificationPreferences => Set<NotificationPreference>();

    // Files / Documents
    public DbSet<Document> Documents => Set<Document>();
    public DbSet<DocumentTemplate> DocumentTemplates => Set<DocumentTemplate>();
    public DbSet<DocumentTypeDefinition> DocumentTypeDefinitions => Set<DocumentTypeDefinition>();
    public DbSet<FileAsset> FileAssets => Set<FileAsset>();

    // System / Integration
    public DbSet<ApiCredential> ApiCredentials => Set<ApiCredential>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<CustomFieldDefinition> CustomFieldDefinitions => Set<CustomFieldDefinition>();
    public DbSet<CustomFieldOption> CustomFieldOptions => Set<CustomFieldOption>();
    public DbSet<CustomFieldValue> CustomFieldValues => Set<CustomFieldValue>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<ImportLog> ImportLogs => Set<ImportLog>();
    public DbSet<ImportLogItem> ImportLogItems => Set<ImportLogItem>();
    public DbSet<NumberSeries> NumberSeries => Set<NumberSeries>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    #endregion

    #region Model Configuration

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        if (modelBuilder == null)
            throw new ArgumentNullException(nameof(modelBuilder));

        base.OnModelCreating(modelBuilder);

        // Optional IEntityTypeConfiguration<T> classes can still extend the model.
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ConfigureIdentity(modelBuilder);
        ConfigureBaseEntityConventions(modelBuilder);
        ConfigureRelationships(modelBuilder);
        ConfigureIndexes(modelBuilder);
        ConfigureCheckConstraints(modelBuilder);
        ConfigureAuditLog(modelBuilder);
        ConfigureSpecialMappings(modelBuilder);
        ApplyDateTimeType(modelBuilder);
        DisableCascadeDelete(modelBuilder);
    }

    private void ConfigureIdentity(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("AspNetUsers");
            entity.HasQueryFilter(x => !x.IsDeleted);
        });

        modelBuilder.Entity<ApplicationRole>(entity =>
        {
            entity.ToTable("AspNetRoles");

            // Identity's default global unique role-name index does not work for tenant roles.
            entity.HasIndex(x => x.NormalizedName)
                .HasDatabaseName("RoleNameIndex")
                .IsUnique(false);

            entity.HasIndex(x => new { x.TenantId, x.NormalizedName })
                .IsUnique()
                .HasFilter("[NormalizedName] IS NOT NULL");

            entity.HasQueryFilter(x =>
                !x.IsDeleted
                && (x.TenantId == null || x.TenantId == CurrentTenantId));
        });
    }

    private void ConfigureBaseEntityConventions(ModelBuilder modelBuilder)
    {
        var entityTypes = modelBuilder.Model.GetEntityTypes()
            .Where(x => x.ClrType != null)
            .ToList();

        foreach (var entityType in entityTypes)
        {
            var clrType = entityType.ClrType;
            var publicIdProperty = entityType.FindProperty("PublicId");
            if (publicIdProperty?.ClrType == typeof(Guid))
                modelBuilder.Entity(clrType).HasIndex("PublicId").IsUnique();

            if (typeof(BaseEntity).IsAssignableFrom(clrType))
            {
                var builder = modelBuilder.Entity(clrType);

                builder.Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion()
                    .IsConcurrencyToken();

                builder.Property(nameof(BaseEntity.CreatedAt))
                    .HasColumnType("datetime2");

                builder.Property(nameof(BaseEntity.UpdatedAt))
                    .HasColumnType("datetime2");

                builder.Property(nameof(BaseEntity.DeletedAt))
                    .HasColumnType("datetime2");

                var parameter = Expression.Parameter(clrType, "e");
                Expression filter = Expression.Not(
                    Expression.Property(parameter, nameof(BaseEntity.IsDeleted)));

                if (typeof(ITenantScopedEntity).IsAssignableFrom(clrType))
                {
                    var entityTenantId = Expression.Property(
                        parameter, nameof(ITenantScopedEntity.TenantId));

                    var currentTenantId = Expression.Property(
                        Expression.Constant(this), nameof(CurrentTenantId));

                    filter = Expression.AndAlso(
                        filter,
                        Expression.Equal(entityTenantId, currentTenantId));
                }

                builder.HasQueryFilter(Expression.Lambda(filter, parameter));
            }

            if (typeof(BaseTenantEntity).IsAssignableFrom(clrType) && !clrType.IsAbstract)
            {
                var builder = modelBuilder.Entity(clrType);

                // Alternate key lets all tenant-owned child FKs enforce same-tenant ownership at DB level.
                builder.HasAlternateKey(
                    nameof(BaseTenantEntity.TenantId),
                    nameof(BaseEntity.Id));

                builder.HasOne(typeof(Tenant), nameof(BaseTenantEntity.Tenant))
                    .WithMany()
                    .HasForeignKey(nameof(BaseTenantEntity.TenantId))
                    .OnDelete(DeleteBehavior.Restrict);
            }
        }

        // Security/audit records with nullable TenantId are safe-by-default for tenant requests.
        modelBuilder.Entity<AuditLog>()
            .HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);

        modelBuilder.Entity<LoginHistory>()
            .HasQueryFilter(x => !x.IsDeleted && x.TenantId == CurrentTenantId);

        modelBuilder.Entity<UserPermission>()
            .HasQueryFilter(x => !x.IsDeleted && (x.TenantId == null || x.TenantId == CurrentTenantId));

        modelBuilder.Entity<UserLegalAcceptance>()
            .HasQueryFilter(x => !x.IsDeleted && (x.TenantId == null || x.TenantId == CurrentTenantId));
    }

    private static void ConfigureRelationships(ModelBuilder modelBuilder)
    {
        // SaaS / Tenant
        Reference<Tenant, InstitutionTypeDefinition>(modelBuilder, nameof(Tenant.InstitutionTypeDefinitionId));
        Reference<ProductModuleFeature, ProductModule>(modelBuilder, nameof(ProductModuleFeature.ProductModuleId));
        Reference<ProductModuleFeature, Feature>(modelBuilder, nameof(ProductModuleFeature.FeatureId));
        Reference<InstitutionTypeModule, InstitutionTypeDefinition>(modelBuilder, nameof(InstitutionTypeModule.InstitutionTypeDefinitionId));
        Reference<InstitutionTypeModule, ProductModule>(modelBuilder, nameof(InstitutionTypeModule.ProductModuleId));
        Reference<TenantModule, ProductModule>(modelBuilder, nameof(TenantModule.ProductModuleId));
        Reference<PlanFeature, SubscriptionPlan>(modelBuilder, nameof(PlanFeature.SubscriptionPlanId));
        Reference<PlanFeature, Feature>(modelBuilder, nameof(PlanFeature.FeatureId));
        Reference<TenantSubscription, SubscriptionPlan>(modelBuilder, nameof(TenantSubscription.SubscriptionPlanId));
        TenantReference<TenantSubscription, TenantSubscription>(modelBuilder, nameof(TenantSubscription.PreviousSubscriptionId));
        TenantReference<SubscriptionInvoice, TenantSubscription>(modelBuilder, nameof(SubscriptionInvoice.TenantSubscriptionId));
        TenantReference<SubscriptionInvoiceLine, SubscriptionInvoice>(modelBuilder, nameof(SubscriptionInvoiceLine.SubscriptionInvoiceId));
        TenantReference<SubscriptionPayment, SubscriptionInvoice>(modelBuilder, nameof(SubscriptionPayment.SubscriptionInvoiceId));
        TenantReference<SubscriptionPayment, FileAsset>(modelBuilder, nameof(SubscriptionPayment.DepositSlipFileId));
        Reference<SubscriptionPayment, ApplicationUser>(modelBuilder, nameof(SubscriptionPayment.VerifiedByUserId));
        Reference<TenantFeature, Feature>(modelBuilder, nameof(TenantFeature.FeatureId));
        Reference<UserLegalAcceptance, ApplicationUser>(modelBuilder, nameof(UserLegalAcceptance.UserId));
        Reference<UserLegalAcceptance, Tenant>(modelBuilder, nameof(UserLegalAcceptance.TenantId));
        Reference<UserLegalAcceptance, LegalDocument>(modelBuilder, nameof(UserLegalAcceptance.LegalDocumentId));
        // Auth / Identity
        Reference<ApplicationUser, Person>(modelBuilder, nameof(ApplicationUser.PersonId));
        Reference<ApplicationRole, Tenant>(modelBuilder, nameof(ApplicationRole.TenantId));
        Reference<TenantMembership, ApplicationUser>(modelBuilder, nameof(TenantMembership.UserId));
        TenantReference<TenantMembership, Campus>(modelBuilder, nameof(TenantMembership.DefaultCampusId));
        TenantReference<TenantMembership, Campus>(modelBuilder, nameof(TenantMembership.LastSelectedCampusId));
        Reference<RolePermission, ApplicationRole>(modelBuilder, nameof(RolePermission.RoleId));
        Reference<RolePermission, Permission>(modelBuilder, nameof(RolePermission.PermissionId));
        Reference<LoginHistory, ApplicationUser>(modelBuilder, nameof(LoginHistory.UserId));
        Reference<LoginHistory, Tenant>(modelBuilder, nameof(LoginHistory.TenantId));
        Reference<RefreshToken, ApplicationUser>(modelBuilder, nameof(RefreshToken.UserId));
        Reference<TwoFactorAuth, ApplicationUser>(modelBuilder, nameof(TwoFactorAuth.UserId));
        Reference<UserPermission, ApplicationUser>(modelBuilder, nameof(UserPermission.UserId));
        Reference<UserPermission, Tenant>(modelBuilder, nameof(UserPermission.TenantId));
        Reference<UserPermission, Permission>(modelBuilder, nameof(UserPermission.PermissionId));
        Reference<UserCampusAccess, ApplicationUser>(modelBuilder, nameof(UserCampusAccess.UserId));
        TenantReference<UserCampusAccess, Campus>(modelBuilder, nameof(UserCampusAccess.CampusId));
        Reference<TenantInvitation, ApplicationRole>(modelBuilder, nameof(TenantInvitation.RoleId));
        TenantReference<TenantInvitation, Campus>(modelBuilder, nameof(TenantInvitation.CampusId));
        Reference<TenantInvitation, ApplicationUser>(modelBuilder, nameof(TenantInvitation.InvitedByUserId));
        Reference<TwoFactorRecoveryCode, ApplicationUser>(modelBuilder, nameof(TwoFactorRecoveryCode.UserId));
        // Global Learner Identity
        Reference<PersonIdentifier, Person>(modelBuilder, nameof(PersonIdentifier.PersonId));
        Reference<PersonAddress, Person>(modelBuilder, nameof(PersonAddress.PersonId));
        Reference<StudentPersonLink, Person>(modelBuilder, nameof(StudentPersonLink.PersonId));
        TenantReference<StudentPersonLink, Student>(modelBuilder, nameof(StudentPersonLink.StudentId));
        Reference<StudentPersonLink, ApplicationUser>(modelBuilder, nameof(StudentPersonLink.LinkedByUserId));
        Reference<StudentPersonLink, ApplicationUser>(modelBuilder, nameof(StudentPersonLink.UnlinkedByUserId));
        Reference<LearnerConsentRequest, Person>(modelBuilder, nameof(LearnerConsentRequest.PersonId));
        TenantReference<LearnerConsentRequest, Student>(modelBuilder, nameof(LearnerConsentRequest.RequestedStudentId));
        Reference<LearnerConsentRequest, ApplicationUser>(modelBuilder, nameof(LearnerConsentRequest.RequestedByUserId));
        Reference<LearnerConsentRequest, ApplicationUser>(modelBuilder, nameof(LearnerConsentRequest.ResolvedByUserId));
        TenantReference<LearnerDataGrant, LearnerConsentRequest>(modelBuilder, nameof(LearnerDataGrant.LearnerConsentRequestId));
        Reference<LearnerDataGrant, Person>(modelBuilder, nameof(LearnerDataGrant.PersonId));
        TenantReference<LearnerDataGrant, Student>(modelBuilder, nameof(LearnerDataGrant.StudentId));
        Reference<LearnerDataGrant, ApplicationUser>(modelBuilder, nameof(LearnerDataGrant.GrantedToUserId));
        Reference<LearnerDataGrant, ApplicationUser>(modelBuilder, nameof(LearnerDataGrant.RevokedByUserId));
        Reference<LearnerIdentityAccessLog, Person>(modelBuilder, nameof(LearnerIdentityAccessLog.PersonId));
        TenantReference<LearnerIdentityAccessLog, Student>(modelBuilder, nameof(LearnerIdentityAccessLog.StudentId));
        Reference<LearnerIdentityAccessLog, ApplicationUser>(modelBuilder, nameof(LearnerIdentityAccessLog.UserId));
        TenantReference<LearnerIdentityAccessLog, LearnerConsentRequest>(modelBuilder, nameof(LearnerIdentityAccessLog.LearnerConsentRequestId));
        Reference<PersonMergeRecord, Person>(modelBuilder, nameof(PersonMergeRecord.SourcePersonId));
        Reference<PersonMergeRecord, Person>(modelBuilder, nameof(PersonMergeRecord.TargetPersonId));
        Reference<PersonMergeRecord, ApplicationUser>(modelBuilder, nameof(PersonMergeRecord.MergedByUserId));
        // Students
        Reference<Student, Person>(modelBuilder, nameof(Student.PersonId));
        Reference<Student, ApplicationUser>(modelBuilder, nameof(Student.UserId));
        TenantReference<Student, AdmissionApplicant>(modelBuilder, nameof(Student.AdmissionApplicantId));
        Reference<Guardian, Person>(modelBuilder, nameof(Guardian.PersonId));
        Reference<Guardian, ApplicationUser>(modelBuilder, nameof(Guardian.UserId));
        TenantReference<StudentGuardian, Student>(modelBuilder, nameof(StudentGuardian.StudentId));
        TenantReference<StudentGuardian, Guardian>(modelBuilder, nameof(StudentGuardian.GuardianId));
        TenantReference<StudentPromotionRecord, Student>(modelBuilder, nameof(StudentPromotionRecord.StudentId));
        TenantReference<StudentPromotionRecord, StudentEnrollment>(modelBuilder, nameof(StudentPromotionRecord.FromEnrollmentId));
        TenantReference<StudentPromotionRecord, StudentEnrollment>(modelBuilder, nameof(StudentPromotionRecord.ToEnrollmentId));
        Reference<StudentPromotionRecord, ApplicationUser>(modelBuilder, nameof(StudentPromotionRecord.ProcessedByUserId));
        TenantReference<StudentExitRecord, Student>(modelBuilder, nameof(StudentExitRecord.StudentId));
        TenantReference<StudentExitRecord, StudentEnrollment>(modelBuilder, nameof(StudentExitRecord.StudentEnrollmentId));
        Reference<StudentExitRecord, ApplicationUser>(modelBuilder, nameof(StudentExitRecord.ProcessedByUserId));
        TenantReference<TransferCertificate, Student>(modelBuilder, nameof(TransferCertificate.StudentId));
        TenantReference<TransferCertificate, StudentEnrollment>(modelBuilder, nameof(TransferCertificate.StudentEnrollmentId));
        Reference<TransferCertificate, TransferRequest>(modelBuilder, nameof(TransferCertificate.TransferRequestId));
        Reference<TransferCertificate, ApplicationUser>(modelBuilder, nameof(TransferCertificate.IssuedByUserId));
        TenantReference<TransferCertificate, FileAsset>(modelBuilder, nameof(TransferCertificate.FileAssetId));
        TenantReference<StudentStatusHistory, Student>(modelBuilder, nameof(StudentStatusHistory.StudentId));
        Reference<StudentStatusHistory, ApplicationUser>(modelBuilder, nameof(StudentStatusHistory.ChangedByUserId));
        // Academic
        TenantReference<AcademicYear, Campus>(modelBuilder, nameof(AcademicYear.CampusId));
        TenantReference<AcademicTerm, AcademicYear>(modelBuilder, nameof(AcademicTerm.AcademicYearId));
        TenantReference<AcademicProgram, AcademicDepartment>(modelBuilder, nameof(AcademicProgram.AcademicDepartmentId));
        TenantReference<ProgramCampus, AcademicProgram>(modelBuilder, nameof(ProgramCampus.AcademicProgramId));
        TenantReference<ProgramCampus, Campus>(modelBuilder, nameof(ProgramCampus.CampusId));
        TenantReference<AcademicLevel, AcademicProgram>(modelBuilder, nameof(AcademicLevel.AcademicProgramId));
        TenantReference<AcademicTrack, AcademicProgram>(modelBuilder, nameof(AcademicTrack.AcademicProgramId));
        TenantReference<AcademicBatch, Campus>(modelBuilder, nameof(AcademicBatch.CampusId));
        TenantReference<AcademicBatch, AcademicYear>(modelBuilder, nameof(AcademicBatch.AcademicYearId));
        TenantReference<AcademicBatch, AcademicTerm>(modelBuilder, nameof(AcademicBatch.AcademicTermId));
        TenantReference<AcademicBatch, AcademicProgram>(modelBuilder, nameof(AcademicBatch.AcademicProgramId));
        TenantReference<AcademicBatch, AcademicLevel>(modelBuilder, nameof(AcademicBatch.AcademicLevelId));
        TenantReference<AcademicBatch, AcademicTrack>(modelBuilder, nameof(AcademicBatch.AcademicTrackId));
        TenantReference<AcademicBatch, Medium>(modelBuilder, nameof(AcademicBatch.MediumId));
        TenantReference<AcademicBatch, Shift>(modelBuilder, nameof(AcademicBatch.ShiftId));
        TenantReference<Room, Campus>(modelBuilder, nameof(Room.CampusId));
        TenantReference<AcademicCurriculum, AcademicProgram>(modelBuilder, nameof(AcademicCurriculum.AcademicProgramId));
        TenantReference<AcademicCurriculum, AcademicTrack>(modelBuilder, nameof(AcademicCurriculum.AcademicTrackId));
        TenantReference<AcademicCurriculum, Medium>(modelBuilder, nameof(AcademicCurriculum.MediumId));
        TenantReference<CurriculumSubject, AcademicCurriculum>(modelBuilder, nameof(CurriculumSubject.AcademicCurriculumId));
        TenantReference<CurriculumSubject, AcademicLevel>(modelBuilder, nameof(CurriculumSubject.AcademicLevelId));
        TenantReference<CurriculumSubject, Subject>(modelBuilder, nameof(CurriculumSubject.SubjectId));
        TenantReference<SubjectPrerequisite, AcademicCurriculum>(modelBuilder, nameof(SubjectPrerequisite.AcademicCurriculumId));
        TenantReference<SubjectPrerequisite, Subject>(modelBuilder, nameof(SubjectPrerequisite.SubjectId));
        TenantReference<SubjectPrerequisite, Subject>(modelBuilder, nameof(SubjectPrerequisite.PrerequisiteSubjectId));
        TenantReference<SubjectOffering, AcademicBatch>(modelBuilder, nameof(SubjectOffering.AcademicBatchId));
        TenantReference<SubjectOffering, CurriculumSubject>(modelBuilder, nameof(SubjectOffering.CurriculumSubjectId));
        TenantReference<SubjectOffering, AcademicYear>(modelBuilder, nameof(SubjectOffering.AcademicYearId));
        TenantReference<SubjectOffering, AcademicTerm>(modelBuilder, nameof(SubjectOffering.AcademicTermId));
        TenantReference<StudentEnrollment, Student>(modelBuilder, nameof(StudentEnrollment.StudentId));
        TenantReference<StudentEnrollment, Campus>(modelBuilder, nameof(StudentEnrollment.CampusId));
        TenantReference<StudentEnrollment, AcademicYear>(modelBuilder, nameof(StudentEnrollment.AcademicYearId));
        TenantReference<StudentEnrollment, AcademicTerm>(modelBuilder, nameof(StudentEnrollment.AcademicTermId));
        TenantReference<StudentEnrollment, AcademicProgram>(modelBuilder, nameof(StudentEnrollment.AcademicProgramId));
        TenantReference<StudentEnrollment, AcademicLevel>(modelBuilder, nameof(StudentEnrollment.AcademicLevelId));
        TenantReference<StudentEnrollment, AcademicBatch>(modelBuilder, nameof(StudentEnrollment.AcademicBatchId));
        TenantReference<StudentEnrollment, AcademicCurriculum>(modelBuilder, nameof(StudentEnrollment.AcademicCurriculumId));
        TenantReference<StudentEnrollment, AcademicTrack>(modelBuilder, nameof(StudentEnrollment.AcademicTrackId));
        TenantReference<StudentEnrollment, Medium>(modelBuilder, nameof(StudentEnrollment.MediumId));
        TenantReference<StudentEnrollment, Shift>(modelBuilder, nameof(StudentEnrollment.ShiftId));
        TenantReference<StudentSubjectRegistration, StudentEnrollment>(modelBuilder, nameof(StudentSubjectRegistration.StudentEnrollmentId));
        TenantReference<StudentSubjectRegistration, SubjectOffering>(modelBuilder, nameof(StudentSubjectRegistration.SubjectOfferingId));
        Reference<StudentSubjectRegistration, ApplicationUser>(modelBuilder, nameof(StudentSubjectRegistration.ApprovedByUserId));
        TenantReference<InstructorAssignment, SubjectOffering>(modelBuilder, nameof(InstructorAssignment.SubjectOfferingId));
        TenantReference<InstructorAssignment, Employee>(modelBuilder, nameof(InstructorAssignment.EmployeeId));
        TenantReference<RoutineEntry, SubjectOffering>(modelBuilder, nameof(RoutineEntry.SubjectOfferingId));
        TenantReference<RoutineEntry, RoutineTimeSlot>(modelBuilder, nameof(RoutineEntry.RoutineTimeSlotId));
        TenantReference<RoutineEntry, InstructorAssignment>(modelBuilder, nameof(RoutineEntry.InstructorAssignmentId));
        TenantReference<RoutineEntry, Room>(modelBuilder, nameof(RoutineEntry.RoomId));
        TenantReference<Substitution, RoutineEntry>(modelBuilder, nameof(Substitution.RoutineEntryId));
        TenantReference<Substitution, Employee>(modelBuilder, nameof(Substitution.SubstituteEmployeeId));
        TenantReference<LessonPlan, SubjectOffering>(modelBuilder, nameof(LessonPlan.SubjectOfferingId));
        TenantReference<LessonPlan, Employee>(modelBuilder, nameof(LessonPlan.EmployeeId));
        Reference<LessonPlan, ApplicationUser>(modelBuilder, nameof(LessonPlan.ReviewedByUserId));
        TenantReference<AcademicCalendarPolicy, Campus>(modelBuilder, nameof(AcademicCalendarPolicy.CampusId));
        TenantReference<AcademicCalendarEvent, Campus>(modelBuilder, nameof(AcademicCalendarEvent.CampusId));
        TenantReference<AcademicCalendarEvent, AcademicYear>(modelBuilder, nameof(AcademicCalendarEvent.AcademicYearId));
        TenantReference<AcademicCalendarEvent, AcademicTerm>(modelBuilder, nameof(AcademicCalendarEvent.AcademicTermId));
        TenantReference<AttendanceSession, AcademicBatch>(modelBuilder, nameof(AttendanceSession.AcademicBatchId));
        TenantReference<AttendanceSession, SubjectOffering>(modelBuilder, nameof(AttendanceSession.SubjectOfferingId));
        TenantReference<AttendanceSession, RoutineEntry>(modelBuilder, nameof(AttendanceSession.RoutineEntryId));
        TenantReference<AttendanceSession, Employee>(modelBuilder, nameof(AttendanceSession.TakenByEmployeeId));
        Reference<AttendanceSession, ApplicationUser>(modelBuilder, nameof(AttendanceSession.FinalizedByUserId));
        // Admission
        TenantReference<AdmissionIntakeForm, Campus>(modelBuilder, nameof(AdmissionIntakeForm.CampusId));
        TenantReference<AdmissionIntakeForm, AcademicYear>(modelBuilder, nameof(AdmissionIntakeForm.AcademicYearId));
        TenantReference<AdmissionIntakeForm, AcademicTerm>(modelBuilder, nameof(AdmissionIntakeForm.AcademicTermId));
        TenantReference<AdmissionIntakeForm, AcademicProgram>(modelBuilder, nameof(AdmissionIntakeForm.AcademicProgramId));
        TenantReference<AdmissionIntakeForm, AcademicLevel>(modelBuilder, nameof(AdmissionIntakeForm.AcademicLevelId));
        TenantReference<AdmissionIntakeForm, AcademicTrack>(modelBuilder, nameof(AdmissionIntakeForm.AcademicTrackId));
        TenantReference<AdmissionIntakeForm, Medium>(modelBuilder, nameof(AdmissionIntakeForm.MediumId));
        TenantReference<AdmissionIntakeForm, Shift>(modelBuilder, nameof(AdmissionIntakeForm.ShiftId));
        TenantReference<AdmissionFormField, AdmissionIntakeForm>(modelBuilder, nameof(AdmissionFormField.AdmissionIntakeFormId));
        TenantReference<AdmissionApplicant, AdmissionIntakeForm>(modelBuilder, nameof(AdmissionApplicant.AdmissionIntakeFormId));
        Reference<AdmissionApplicant, Person>(modelBuilder, nameof(AdmissionApplicant.PersonId));
        Reference<AdmissionApplicant, ApplicationUser>(modelBuilder, nameof(AdmissionApplicant.ReviewedByUserId));
        TenantReference<AdmissionApplicant, Student>(modelBuilder, nameof(AdmissionApplicant.ConvertedStudentId));
        TenantReference<AdmissionApplicant, StudentEnrollment>(modelBuilder, nameof(AdmissionApplicant.ConvertedEnrollmentId));
        TenantReference<AdmissionApplicantFieldValue, AdmissionApplicant>(modelBuilder, nameof(AdmissionApplicantFieldValue.AdmissionApplicantId));
        TenantReference<AdmissionApplicantFieldValue, AdmissionFormField>(modelBuilder, nameof(AdmissionApplicantFieldValue.AdmissionFormFieldId));
        TenantReference<AdmissionApplicantGuardian, AdmissionApplicant>(modelBuilder, nameof(AdmissionApplicantGuardian.AdmissionApplicantId));
        TenantReference<AdmissionApplicantDocument, AdmissionApplicant>(modelBuilder, nameof(AdmissionApplicantDocument.AdmissionApplicantId));
        TenantReference<AdmissionApplicantDocument, FileAsset>(modelBuilder, nameof(AdmissionApplicantDocument.FileAssetId));
        Reference<AdmissionApplicantDocument, ApplicationUser>(modelBuilder, nameof(AdmissionApplicantDocument.VerifiedByUserId));
        TenantReference<AdmissionTest, AdmissionIntakeForm>(modelBuilder, nameof(AdmissionTest.AdmissionIntakeFormId));
        TenantReference<AdmissionResult, AdmissionTest>(modelBuilder, nameof(AdmissionResult.AdmissionTestId));
        TenantReference<AdmissionResult, AdmissionApplicant>(modelBuilder, nameof(AdmissionResult.AdmissionApplicantId));
        TenantReference<AdmissionDecision, AdmissionApplicant>(modelBuilder, nameof(AdmissionDecision.AdmissionApplicantId));
        TenantReference<AdmissionDecision, AcademicBatch>(modelBuilder, nameof(AdmissionDecision.OfferedAcademicBatchId));
        Reference<AdmissionDecision, ApplicationUser>(modelBuilder, nameof(AdmissionDecision.DecidedByUserId));
        TenantReference<AdmissionPayment, AdmissionApplicant>(modelBuilder, nameof(AdmissionPayment.AdmissionApplicantId));
        Reference<AdmissionPayment, ApplicationUser>(modelBuilder, nameof(AdmissionPayment.ReceivedByUserId));
        TenantReference<AdmissionPayment, Journal>(modelBuilder, nameof(AdmissionPayment.JournalId));
        // Assessment / Result
        TenantReference<Assessment, Campus>(modelBuilder, nameof(Assessment.CampusId));
        TenantReference<Assessment, AcademicYear>(modelBuilder, nameof(Assessment.AcademicYearId));
        TenantReference<Assessment, AcademicTerm>(modelBuilder, nameof(Assessment.AcademicTermId));
        TenantReference<Assessment, GradeScheme>(modelBuilder, nameof(Assessment.GradeSchemeId));
        TenantReference<AssessmentSubject, Assessment>(modelBuilder, nameof(AssessmentSubject.AssessmentId));
        TenantReference<AssessmentSubject, SubjectOffering>(modelBuilder, nameof(AssessmentSubject.SubjectOfferingId));
        TenantReference<AssessmentSubject, GradeScheme>(modelBuilder, nameof(AssessmentSubject.GradeSchemeId));
        TenantReference<AssessmentSchedule, AssessmentSubject>(modelBuilder, nameof(AssessmentSchedule.AssessmentSubjectId));
        TenantReference<AssessmentSchedule, Room>(modelBuilder, nameof(AssessmentSchedule.RoomId));
        TenantReference<StudentAssessmentMark, AssessmentSubject>(modelBuilder, nameof(StudentAssessmentMark.AssessmentSubjectId));
        TenantReference<StudentAssessmentMark, StudentSubjectRegistration>(modelBuilder, nameof(StudentAssessmentMark.StudentSubjectRegistrationId));
        Reference<StudentAssessmentMark, ApplicationUser>(modelBuilder, nameof(StudentAssessmentMark.EnteredByUserId));
        TenantReference<GradeScheme, AcademicProgram>(modelBuilder, nameof(GradeScheme.AcademicProgramId));
        TenantReference<GradeRule, GradeScheme>(modelBuilder, nameof(GradeRule.GradeSchemeId));
        TenantReference<ResultPublication, Assessment>(modelBuilder, nameof(ResultPublication.AssessmentId));
        TenantReference<ResultPublication, AcademicBatch>(modelBuilder, nameof(ResultPublication.AcademicBatchId));
        Reference<ResultPublication, ApplicationUser>(modelBuilder, nameof(ResultPublication.PublishedByUserId));
        TenantReference<CertificateIssue, CertificateTemplate>(modelBuilder, nameof(CertificateIssue.CertificateTemplateId));
        TenantReference<CertificateIssue, Student>(modelBuilder, nameof(CertificateIssue.StudentId));
        TenantReference<CertificateIssue, StudentEnrollment>(modelBuilder, nameof(CertificateIssue.StudentEnrollmentId));
        Reference<CertificateIssue, ApplicationUser>(modelBuilder, nameof(CertificateIssue.IssuedByUserId));
        TenantReference<CertificateIssue, FileAsset>(modelBuilder, nameof(CertificateIssue.FileAssetId));
        TenantReference<AssessmentComponent, AssessmentSubject>(modelBuilder, nameof(AssessmentComponent.AssessmentSubjectId));
        TenantReference<StudentAssessmentComponentMark, AssessmentComponent>(modelBuilder, nameof(StudentAssessmentComponentMark.AssessmentComponentId));
        TenantReference<StudentAssessmentComponentMark, StudentAssessmentMark>(modelBuilder, nameof(StudentAssessmentComponentMark.StudentAssessmentMarkId));
        Reference<StudentAssessmentComponentMark, ApplicationUser>(modelBuilder, nameof(StudentAssessmentComponentMark.EnteredByUserId));
        TenantReference<StudentResultSummary, ResultPublication>(modelBuilder, nameof(StudentResultSummary.ResultPublicationId));
        TenantReference<StudentResultSummary, Assessment>(modelBuilder, nameof(StudentResultSummary.AssessmentId));
        TenantReference<StudentResultSummary, StudentEnrollment>(modelBuilder, nameof(StudentResultSummary.StudentEnrollmentId));
        TenantReference<TranscriptIssue, Student>(modelBuilder, nameof(TranscriptIssue.StudentId));
        Reference<TranscriptIssue, ApplicationUser>(modelBuilder, nameof(TranscriptIssue.IssuedByUserId));
        TenantReference<TranscriptIssue, FileAsset>(modelBuilder, nameof(TranscriptIssue.FileAssetId));
        // Attendance / Leave
        TenantReference<StudentAttendance, AttendanceSession>(modelBuilder, nameof(StudentAttendance.AttendanceSessionId));
        TenantReference<StudentAttendance, StudentEnrollment>(modelBuilder, nameof(StudentAttendance.StudentEnrollmentId));
        Reference<StudentAttendance, ApplicationUser>(modelBuilder, nameof(StudentAttendance.RecordedByUserId));
        TenantReference<EmployeeAttendance, Employee>(modelBuilder, nameof(EmployeeAttendance.EmployeeId));
        Reference<EmployeeAttendance, ApplicationUser>(modelBuilder, nameof(EmployeeAttendance.RecordedByUserId));
        TenantReference<EmployeeLeaveApplication, Employee>(modelBuilder, nameof(EmployeeLeaveApplication.EmployeeId));
        TenantReference<EmployeeLeaveApplication, LeaveType>(modelBuilder, nameof(EmployeeLeaveApplication.LeaveTypeId));
        TenantReference<EmployeeLeaveApplication, FileAsset>(modelBuilder, nameof(EmployeeLeaveApplication.AttachmentFileId));
        Reference<EmployeeLeaveApplication, ApplicationUser>(modelBuilder, nameof(EmployeeLeaveApplication.ReviewedByUserId));
        TenantReference<StudentLeaveApplication, StudentEnrollment>(modelBuilder, nameof(StudentLeaveApplication.StudentEnrollmentId));
        TenantReference<StudentLeaveApplication, FileAsset>(modelBuilder, nameof(StudentLeaveApplication.AttachmentFileId));
        Reference<StudentLeaveApplication, ApplicationUser>(modelBuilder, nameof(StudentLeaveApplication.ReviewedByUserId));
        TenantReference<EmployeeLeaveEntitlement, Employee>(modelBuilder, nameof(EmployeeLeaveEntitlement.EmployeeId));
        TenantReference<EmployeeLeaveEntitlement, LeaveType>(modelBuilder, nameof(EmployeeLeaveEntitlement.LeaveTypeId));
        TenantReference<EmployeeLeaveAdjustment, Employee>(modelBuilder, nameof(EmployeeLeaveAdjustment.EmployeeId));
        TenantReference<EmployeeLeaveAdjustment, LeaveType>(modelBuilder, nameof(EmployeeLeaveAdjustment.LeaveTypeId));
        Reference<EmployeeLeaveAdjustment, ApplicationUser>(modelBuilder, nameof(EmployeeLeaveAdjustment.ApprovedByUserId));
        TenantReference<StudentAttendanceAdjustment, StudentAttendance>(modelBuilder, nameof(StudentAttendanceAdjustment.StudentAttendanceId));
        Reference<StudentAttendanceAdjustment, ApplicationUser>(modelBuilder, nameof(StudentAttendanceAdjustment.ChangedByUserId));
        TenantReference<EmployeeAttendanceAdjustment, EmployeeAttendance>(modelBuilder, nameof(EmployeeAttendanceAdjustment.EmployeeAttendanceId));
        Reference<EmployeeAttendanceAdjustment, ApplicationUser>(modelBuilder, nameof(EmployeeAttendanceAdjustment.ChangedByUserId));
        // HR
        TenantReference<OrganizationUnit, Campus>(modelBuilder, nameof(OrganizationUnit.CampusId));
        TenantReference<OrganizationUnit, OrganizationUnit>(modelBuilder, nameof(OrganizationUnit.ParentId));
        Reference<Employee, Person>(modelBuilder, nameof(Employee.PersonId));
        Reference<Employee, ApplicationUser>(modelBuilder, nameof(Employee.UserId));
        TenantReference<Employee, OrganizationUnit>(modelBuilder, nameof(Employee.OrganizationUnitId));
        TenantReference<Employee, Designation>(modelBuilder, nameof(Employee.DesignationId));
        TenantReference<EmployeeShiftAssignment, Employee>(modelBuilder, nameof(EmployeeShiftAssignment.EmployeeId));
        TenantReference<EmployeeShiftAssignment, WorkShift>(modelBuilder, nameof(EmployeeShiftAssignment.WorkShiftId));
        TenantReference<EmployeeCampusAssignment, Employee>(modelBuilder, nameof(EmployeeCampusAssignment.EmployeeId));
        TenantReference<EmployeeCampusAssignment, Campus>(modelBuilder, nameof(EmployeeCampusAssignment.CampusId));
        TenantReference<EmployeeBankAccount, Employee>(modelBuilder, nameof(EmployeeBankAccount.EmployeeId));
        TenantReference<EmployeeAssignmentHistory, Employee>(modelBuilder, nameof(EmployeeAssignmentHistory.EmployeeId));
        TenantReference<EmployeeAssignmentHistory, Campus>(modelBuilder, nameof(EmployeeAssignmentHistory.CampusId));
        TenantReference<EmployeeAssignmentHistory, OrganizationUnit>(modelBuilder, nameof(EmployeeAssignmentHistory.OrganizationUnitId));
        TenantReference<EmployeeAssignmentHistory, Designation>(modelBuilder, nameof(EmployeeAssignmentHistory.DesignationId));
        // Student Finance
        TenantReference<FeeHead, Account>(modelBuilder, nameof(FeeHead.IncomeAccountId));
        TenantReference<FeeHead, Account>(modelBuilder, nameof(FeeHead.ReceivableAccountId));
        TenantReference<FeeStructure, Campus>(modelBuilder, nameof(FeeStructure.CampusId));
        TenantReference<FeeStructure, AcademicYear>(modelBuilder, nameof(FeeStructure.AcademicYearId));
        TenantReference<FeeStructure, AcademicProgram>(modelBuilder, nameof(FeeStructure.AcademicProgramId));
        TenantReference<FeeStructure, AcademicLevel>(modelBuilder, nameof(FeeStructure.AcademicLevelId));
        TenantReference<FeeStructure, AcademicBatch>(modelBuilder, nameof(FeeStructure.AcademicBatchId));
        TenantReference<FeeStructureLine, FeeStructure>(modelBuilder, nameof(FeeStructureLine.FeeStructureId));
        TenantReference<FeeStructureLine, FeeHead>(modelBuilder, nameof(FeeStructureLine.FeeHeadId));
        TenantReference<StudentInvoice, StudentEnrollment>(modelBuilder, nameof(StudentInvoice.StudentEnrollmentId));
        TenantReference<StudentInvoice, Journal>(modelBuilder, nameof(StudentInvoice.JournalId));
        TenantReference<StudentInvoiceLine, StudentInvoice>(modelBuilder, nameof(StudentInvoiceLine.StudentInvoiceId));
        TenantReference<StudentInvoiceLine, FeeHead>(modelBuilder, nameof(StudentInvoiceLine.FeeHeadId));
        TenantReference<StudentPayment, Student>(modelBuilder, nameof(StudentPayment.StudentId));
        TenantReference<StudentPayment, BankAccount>(modelBuilder, nameof(StudentPayment.BankAccountId));
        Reference<StudentPayment, ApplicationUser>(modelBuilder, nameof(StudentPayment.ReceivedByUserId));
        TenantReference<StudentPayment, Journal>(modelBuilder, nameof(StudentPayment.JournalId));
        TenantReference<PaymentAllocation, StudentPayment>(modelBuilder, nameof(PaymentAllocation.StudentPaymentId));
        TenantReference<PaymentAllocation, StudentInvoice>(modelBuilder, nameof(PaymentAllocation.StudentInvoiceId));
        TenantReference<DiscountRule, FeeHead>(modelBuilder, nameof(DiscountRule.FeeHeadId));
        TenantReference<StudentDiscount, StudentEnrollment>(modelBuilder, nameof(StudentDiscount.StudentEnrollmentId));
        TenantReference<StudentDiscount, DiscountRule>(modelBuilder, nameof(StudentDiscount.DiscountRuleId));
        Reference<StudentDiscount, ApplicationUser>(modelBuilder, nameof(StudentDiscount.ApprovedByUserId));
        TenantReference<FineRule, FeeHead>(modelBuilder, nameof(FineRule.FeeHeadId));
        TenantReference<StudentFine, StudentEnrollment>(modelBuilder, nameof(StudentFine.StudentEnrollmentId));
        TenantReference<StudentFine, FineRule>(modelBuilder, nameof(StudentFine.FineRuleId));
        Reference<StudentFine, ApplicationUser>(modelBuilder, nameof(StudentFine.WaivedByUserId));
        TenantReference<Refund, StudentPayment>(modelBuilder, nameof(Refund.StudentPaymentId));
        Reference<Refund, ApplicationUser>(modelBuilder, nameof(Refund.RequestedByUserId));
        Reference<Refund, ApplicationUser>(modelBuilder, nameof(Refund.ApprovedByUserId));
        TenantReference<Refund, Journal>(modelBuilder, nameof(Refund.ReversalJournalId));
        TenantReference<RefundAllocation, Refund>(modelBuilder, nameof(RefundAllocation.RefundId));
        TenantReference<RefundAllocation, PaymentAllocation>(modelBuilder, nameof(RefundAllocation.PaymentAllocationId));
        // Accounting
        TenantReference<Account, Account>(modelBuilder, nameof(Account.ParentAccountId));
        TenantReference<BankAccount, Account>(modelBuilder, nameof(BankAccount.AccountId));
        TenantReference<BankAccount, Campus>(modelBuilder, nameof(BankAccount.CampusId));
        TenantReference<Journal, AccountingPeriod>(modelBuilder, nameof(Journal.AccountingPeriodId));
        TenantReference<Journal, Campus>(modelBuilder, nameof(Journal.CampusId));
        TenantReference<Journal, Journal>(modelBuilder, nameof(Journal.ReversalOfJournalId));
        Reference<Journal, ApplicationUser>(modelBuilder, nameof(Journal.ApprovedByUserId));
        Reference<Journal, ApplicationUser>(modelBuilder, nameof(Journal.PostedByUserId));
        Reference<AccountingPeriod, ApplicationUser>(modelBuilder, nameof(AccountingPeriod.ClosedByUserId));
        TenantReference<JournalLine, Journal>(modelBuilder, nameof(JournalLine.JournalId));
        TenantReference<JournalLine, Account>(modelBuilder, nameof(JournalLine.AccountId));
        TenantReference<JournalLine, Campus>(modelBuilder, nameof(JournalLine.CampusId));
        TenantReference<JournalLine, Student>(modelBuilder, nameof(JournalLine.StudentId));
        TenantReference<JournalLine, Employee>(modelBuilder, nameof(JournalLine.EmployeeId));
        TenantReference<JournalLine, AcademicProgram>(modelBuilder, nameof(JournalLine.AcademicProgramId));
        TenantReference<JournalLine, FeeHead>(modelBuilder, nameof(JournalLine.FeeHeadId));
        // Payroll
        TenantReference<SalaryStructure, Employee>(modelBuilder, nameof(SalaryStructure.EmployeeId));
        TenantReference<SalaryStructureLine, SalaryStructure>(modelBuilder, nameof(SalaryStructureLine.SalaryStructureId));
        TenantReference<SalaryStructureLine, SalaryComponent>(modelBuilder, nameof(SalaryStructureLine.SalaryComponentId));
        TenantReference<SalaryStructureLine, SalaryComponent>(modelBuilder, nameof(SalaryStructureLine.BasedOnSalaryComponentId));
        TenantReference<PayrollRun, Campus>(modelBuilder, nameof(PayrollRun.CampusId));
        Reference<PayrollRun, ApplicationUser>(modelBuilder, nameof(PayrollRun.ApprovedByUserId));
        TenantReference<PayrollRun, Journal>(modelBuilder, nameof(PayrollRun.JournalId));
        TenantReference<PayrollEmployee, PayrollRun>(modelBuilder, nameof(PayrollEmployee.PayrollRunId));
        TenantReference<PayrollEmployee, Employee>(modelBuilder, nameof(PayrollEmployee.EmployeeId));
        TenantReference<PayrollLine, PayrollEmployee>(modelBuilder, nameof(PayrollLine.PayrollEmployeeId));
        TenantReference<PayrollLine, SalaryComponent>(modelBuilder, nameof(PayrollLine.SalaryComponentId));
        TenantReference<Bonus, Employee>(modelBuilder, nameof(Bonus.EmployeeId));
        TenantReference<LoanAdvance, Employee>(modelBuilder, nameof(LoanAdvance.EmployeeId));
        TenantReference<LoanAdvanceRecovery, LoanAdvance>(modelBuilder, nameof(LoanAdvanceRecovery.LoanAdvanceId));
        TenantReference<LoanAdvanceRecovery, PayrollEmployee>(modelBuilder, nameof(LoanAdvanceRecovery.PayrollEmployeeId));
        TenantReference<LoanAdvanceRecovery, Journal>(modelBuilder, nameof(LoanAdvanceRecovery.JournalId));
        TenantReference<PayrollPayment, PayrollEmployee>(modelBuilder, nameof(PayrollPayment.PayrollEmployeeId));
        TenantReference<PayrollPayment, BankAccount>(modelBuilder, nameof(PayrollPayment.BankAccountId));
        TenantReference<PayrollPayment, EmployeeBankAccount>(modelBuilder, nameof(PayrollPayment.EmployeeBankAccountId));
        TenantReference<PayrollPayment, Journal>(modelBuilder, nameof(PayrollPayment.JournalId));
        // Library
        TenantReference<Book, BookCategory>(modelBuilder, nameof(Book.BookCategoryId));
        TenantReference<BookCopy, Book>(modelBuilder, nameof(BookCopy.BookId));
        TenantReference<BookCopy, LibraryBranch>(modelBuilder, nameof(BookCopy.LibraryBranchId));
        TenantReference<BookIssue, BookCopy>(modelBuilder, nameof(BookIssue.BookCopyId));
        TenantReference<BookIssue, Student>(modelBuilder, nameof(BookIssue.StudentId));
        Reference<BookIssue, ApplicationUser>(modelBuilder, nameof(BookIssue.IssuedByUserId));
        Reference<BookIssue, ApplicationUser>(modelBuilder, nameof(BookIssue.ReturnedByUserId));
        TenantReference<BookReservation, Book>(modelBuilder, nameof(BookReservation.BookId));
        TenantReference<BookReservation, Student>(modelBuilder, nameof(BookReservation.StudentId));
        TenantReference<LibraryBranch, Campus>(modelBuilder, nameof(LibraryBranch.CampusId));
        // Transport
        TenantReference<Route, Campus>(modelBuilder, nameof(Route.CampusId));
        TenantReference<RouteStop, Route>(modelBuilder, nameof(RouteStop.RouteId));
        TenantReference<Vehicle, Campus>(modelBuilder, nameof(Vehicle.CampusId));
        TenantReference<StudentTransport, Student>(modelBuilder, nameof(StudentTransport.StudentId));
        TenantReference<StudentTransport, StudentEnrollment>(modelBuilder, nameof(StudentTransport.StudentEnrollmentId));
        TenantReference<StudentTransport, RouteVehicleAssignment>(modelBuilder, nameof(StudentTransport.RouteVehicleAssignmentId));
        TenantReference<StudentTransport, Route>(modelBuilder, nameof(StudentTransport.RouteId));
        TenantReference<StudentTransport, Vehicle>(modelBuilder, nameof(StudentTransport.VehicleId));
        TenantReference<StudentTransport, RouteStop>(modelBuilder, nameof(StudentTransport.PickupStopId));
        TenantReference<StudentTransport, RouteStop>(modelBuilder, nameof(StudentTransport.DropStopId));
        TenantReference<TransportDriver, Employee>(modelBuilder, nameof(TransportDriver.EmployeeId));
        TenantReference<VehicleDriverAssignment, Vehicle>(modelBuilder, nameof(VehicleDriverAssignment.VehicleId));
        TenantReference<VehicleDriverAssignment, TransportDriver>(modelBuilder, nameof(VehicleDriverAssignment.TransportDriverId));
        TenantReference<RouteVehicleAssignment, Route>(modelBuilder, nameof(RouteVehicleAssignment.RouteId));
        TenantReference<RouteVehicleAssignment, Vehicle>(modelBuilder, nameof(RouteVehicleAssignment.VehicleId));
        // Hostel
        TenantReference<Hostel, Campus>(modelBuilder, nameof(Hostel.CampusId));
        TenantReference<HostelRoom, Hostel>(modelBuilder, nameof(HostelRoom.HostelId));
        TenantReference<HostelBed, HostelRoom>(modelBuilder, nameof(HostelBed.HostelRoomId));
        TenantReference<StudentHostelAllocation, Student>(modelBuilder, nameof(StudentHostelAllocation.StudentId));
        TenantReference<StudentHostelAllocation, StudentEnrollment>(modelBuilder, nameof(StudentHostelAllocation.StudentEnrollmentId));
        TenantReference<StudentHostelAllocation, HostelBed>(modelBuilder, nameof(StudentHostelAllocation.HostelBedId));
        TenantReference<StudentHostelAllocation, FeeHead>(modelBuilder, nameof(StudentHostelAllocation.FeeHeadId));
        // LMS
        TenantReference<Course, AcademicProgram>(modelBuilder, nameof(Course.AcademicProgramId));
        TenantReference<Course, Subject>(modelBuilder, nameof(Course.SubjectId));
        TenantReference<Course, Employee>(modelBuilder, nameof(Course.PrimaryInstructorEmployeeId));
        TenantReference<CourseEnrollment, Course>(modelBuilder, nameof(CourseEnrollment.CourseId));
        TenantReference<CourseEnrollment, Student>(modelBuilder, nameof(CourseEnrollment.StudentId));
        TenantReference<CourseEnrollment, StudentEnrollment>(modelBuilder, nameof(CourseEnrollment.StudentEnrollmentId));
        TenantReference<CourseSection, Course>(modelBuilder, nameof(CourseSection.CourseId));
        TenantReference<Lesson, Course>(modelBuilder, nameof(Lesson.CourseId));
        TenantReference<Lesson, CourseSection>(modelBuilder, nameof(Lesson.CourseSectionId));
        TenantReference<LessonProgress, CourseEnrollment>(modelBuilder, nameof(LessonProgress.CourseEnrollmentId));
        TenantReference<LessonProgress, Lesson>(modelBuilder, nameof(LessonProgress.LessonId));
        TenantReference<Assignment, Course>(modelBuilder, nameof(Assignment.CourseId));
        TenantReference<AssignmentAttachment, Assignment>(modelBuilder, nameof(AssignmentAttachment.AssignmentId));
        TenantReference<AssignmentAttachment, FileAsset>(modelBuilder, nameof(AssignmentAttachment.FileAssetId));
        TenantReference<AssignmentSubmission, Assignment>(modelBuilder, nameof(AssignmentSubmission.AssignmentId));
        TenantReference<AssignmentSubmission, CourseEnrollment>(modelBuilder, nameof(AssignmentSubmission.CourseEnrollmentId));
        TenantReference<AssignmentSubmission, FileAsset>(modelBuilder, nameof(AssignmentSubmission.FileAssetId));
        Reference<AssignmentSubmission, ApplicationUser>(modelBuilder, nameof(AssignmentSubmission.GradedByUserId));
        TenantReference<AssignmentSubmissionFile, AssignmentSubmission>(modelBuilder, nameof(AssignmentSubmissionFile.AssignmentSubmissionId));
        TenantReference<AssignmentSubmissionFile, FileAsset>(modelBuilder, nameof(AssignmentSubmissionFile.FileAssetId));
        TenantReference<Quiz, Course>(modelBuilder, nameof(Quiz.CourseId));
        TenantReference<Quiz, Quiz>(modelBuilder, nameof(Quiz.PreviousVersionId));
        TenantReference<QuizQuestion, Quiz>(modelBuilder, nameof(QuizQuestion.QuizId));
        TenantReference<QuizOption, QuizQuestion>(modelBuilder, nameof(QuizOption.QuizQuestionId));
        TenantReference<QuizAttempt, Quiz>(modelBuilder, nameof(QuizAttempt.QuizId));
        TenantReference<QuizAttempt, CourseEnrollment>(modelBuilder, nameof(QuizAttempt.CourseEnrollmentId));
        TenantReference<QuizAnswer, QuizAttempt>(modelBuilder, nameof(QuizAnswer.QuizAttemptId));
        TenantReference<QuizAnswer, QuizQuestion>(modelBuilder, nameof(QuizAnswer.QuizQuestionId));
        TenantReference<QuizAnswer, QuizOption>(modelBuilder, nameof(QuizAnswer.SelectedOptionId));
        TenantReference<QuizAnswerOption, QuizAnswer>(modelBuilder, nameof(QuizAnswerOption.QuizAnswerId));
        TenantReference<QuizAnswerOption, QuizOption>(modelBuilder, nameof(QuizAnswerOption.QuizOptionId));
        TenantReference<LiveClass, Course>(modelBuilder, nameof(LiveClass.CourseId));
        TenantReference<LiveClass, Employee>(modelBuilder, nameof(LiveClass.InstructorEmployeeId));
        TenantReference<LessonResource, Lesson>(modelBuilder, nameof(LessonResource.LessonId));
        TenantReference<LessonResource, FileAsset>(modelBuilder, nameof(LessonResource.FileAssetId));
        // Communication
        TenantReference<Notice, NoticeCategory>(modelBuilder, nameof(Notice.NoticeCategoryId));
        Reference<Notification, ApplicationUser>(modelBuilder, nameof(Notification.UserId));
        Reference<NotificationPreference, ApplicationUser>(modelBuilder, nameof(NotificationPreference.UserId));
        TenantReference<Message, MessageThread>(modelBuilder, nameof(Message.MessageThreadId));
        Reference<Message, ApplicationUser>(modelBuilder, nameof(Message.SenderUserId));
        Reference<Message, ApplicationUser>(modelBuilder, nameof(Message.RecipientUserId));
        Reference<DeviceToken, ApplicationUser>(modelBuilder, nameof(DeviceToken.UserId));
        TenantReference<CommunicationDelivery, CommunicationGateway>(modelBuilder, nameof(CommunicationDelivery.CommunicationGatewayId));
        Reference<CommunicationDelivery, ApplicationUser>(modelBuilder, nameof(CommunicationDelivery.UserId));
        TenantReference<NoticeAudience, Notice>(modelBuilder, nameof(NoticeAudience.NoticeId));
        TenantReference<NoticeAudience, Campus>(modelBuilder, nameof(NoticeAudience.CampusId));
        TenantReference<NoticeAudience, AcademicProgram>(modelBuilder, nameof(NoticeAudience.AcademicProgramId));
        TenantReference<NoticeAudience, AcademicBatch>(modelBuilder, nameof(NoticeAudience.AcademicBatchId));
        Reference<NoticeAudience, ApplicationUser>(modelBuilder, nameof(NoticeAudience.UserId));
        TenantReference<NoticeReadReceipt, Notice>(modelBuilder, nameof(NoticeReadReceipt.NoticeId));
        Reference<NoticeReadReceipt, ApplicationUser>(modelBuilder, nameof(NoticeReadReceipt.UserId));
        TenantReference<MessageThreadParticipant, MessageThread>(modelBuilder, nameof(MessageThreadParticipant.MessageThreadId));
        Reference<MessageThreadParticipant, ApplicationUser>(modelBuilder, nameof(MessageThreadParticipant.UserId));
        TenantReference<MessageAttachment, Message>(modelBuilder, nameof(MessageAttachment.MessageId));
        TenantReference<MessageAttachment, FileAsset>(modelBuilder, nameof(MessageAttachment.FileAssetId));
        // Files / Documents
        Reference<FileAsset, ApplicationUser>(modelBuilder, nameof(FileAsset.UploadedByUserId));
        TenantReference<Document, FileAsset>(modelBuilder, nameof(Document.FileAssetId));
        TenantReference<Document, Document>(modelBuilder, nameof(Document.SupersedesDocumentId));
        // System / Integration
        TenantReference<CustomFieldOption, CustomFieldDefinition>(modelBuilder, nameof(CustomFieldOption.CustomFieldDefinitionId));
        TenantReference<CustomFieldValue, CustomFieldDefinition>(modelBuilder, nameof(CustomFieldValue.CustomFieldDefinitionId));
        TenantReference<ImportLog, FileAsset>(modelBuilder, nameof(ImportLog.FileAssetId));
        TenantReference<ImportLogItem, ImportLog>(modelBuilder, nameof(ImportLogItem.ImportLogId));
        TenantReference<WebhookDelivery, WebhookEndpoint>(modelBuilder, nameof(WebhookDelivery.WebhookEndpointId));
        // Inventory / Assets
        TenantReference<InventoryLocation, Campus>(modelBuilder, nameof(InventoryLocation.CampusId));
        TenantReference<InventoryMovement, InventoryLocation>(modelBuilder, nameof(InventoryMovement.FromLocationId));
        TenantReference<InventoryMovement, InventoryLocation>(modelBuilder, nameof(InventoryMovement.ToLocationId));
        Reference<InventoryMovement, ApplicationUser>(modelBuilder, nameof(InventoryMovement.PostedByUserId));
        TenantReference<InventoryMovement, InventoryMovement>(modelBuilder, nameof(InventoryMovement.ReversalOfMovementId));
        TenantReference<InventoryMovementLine, InventoryMovement>(modelBuilder, nameof(InventoryMovementLine.InventoryMovementId));
        TenantReference<InventoryMovementLine, InventoryItem>(modelBuilder, nameof(InventoryMovementLine.InventoryItemId));
        TenantReference<Asset, AssetCategory>(modelBuilder, nameof(Asset.AssetCategoryId));
        TenantReference<Asset, InventoryItem>(modelBuilder, nameof(Asset.InventoryItemId));
        TenantReference<Asset, Campus>(modelBuilder, nameof(Asset.CampusId));
        TenantReference<Asset, InventoryLocation>(modelBuilder, nameof(Asset.InventoryLocationId));
        TenantReference<AssetAssignment, Asset>(modelBuilder, nameof(AssetAssignment.AssetId));
        TenantReference<AssetAssignment, Employee>(modelBuilder, nameof(AssetAssignment.EmployeeId));
        TenantReference<AssetAssignment, Student>(modelBuilder, nameof(AssetAssignment.StudentId));
        TenantReference<AssetAssignment, OrganizationUnit>(modelBuilder, nameof(AssetAssignment.OrganizationUnitId));
        TenantReference<AssetMaintenance, Asset>(modelBuilder, nameof(AssetMaintenance.AssetId));

        // Document types use stable tenant-local codes rather than numeric IDs.
        modelBuilder.Entity<DocumentTypeDefinition>()
            .HasAlternateKey(x => new { x.TenantId, x.Code });

        modelBuilder.Entity<Document>()
            .HasOne<DocumentTypeDefinition>()
            .WithMany()
            .HasForeignKey(x => new { x.TenantId, x.DocumentTypeCode })
            .HasPrincipalKey(x => new { x.TenantId, x.Code })
            .OnDelete(DeleteBehavior.Restrict);

        // Cross-tenant transfer is deliberately global. Composite FKs preserve the source/destination tenant boundary.
        Reference<TransferRequest, Tenant>(modelBuilder, nameof(TransferRequest.SourceTenantId));
        Reference<TransferRequest, Tenant>(modelBuilder, nameof(TransferRequest.DestinationTenantId));
        Reference<TransferRequest, ApplicationUser>(modelBuilder, nameof(TransferRequest.RequestedByUserId));
        Reference<TransferRequest, ApplicationUser>(modelBuilder, nameof(TransferRequest.AcceptedByUserId));
        Reference<TransferRequest, ApplicationUser>(modelBuilder, nameof(TransferRequest.RejectedByUserId));

        modelBuilder.Entity<TransferRequest>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(nameof(TransferRequest.SourceTenantId), nameof(TransferRequest.SourceStudentId))
            .HasPrincipalKey(nameof(BaseTenantEntity.TenantId), nameof(BaseEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TransferRequest>()
            .HasOne<StudentEnrollment>()
            .WithMany()
            .HasForeignKey(nameof(TransferRequest.SourceTenantId), nameof(TransferRequest.SourceEnrollmentId))
            .HasPrincipalKey(nameof(BaseTenantEntity.TenantId), nameof(BaseEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TransferRequest>()
            .HasOne<Student>()
            .WithMany()
            .HasForeignKey(nameof(TransferRequest.DestinationTenantId), nameof(TransferRequest.DestinationStudentId))
            .HasPrincipalKey(nameof(BaseTenantEntity.TenantId), nameof(BaseEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void Reference<TEntity, TPrincipal>(
        ModelBuilder modelBuilder,
        string foreignKey)
        where TEntity : class
        where TPrincipal : class
    {
        modelBuilder.Entity<TEntity>()
            .HasOne<TPrincipal>()
            .WithMany()
            .HasForeignKey(foreignKey)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void TenantReference<TEntity, TPrincipal>(
        ModelBuilder modelBuilder,
        string foreignKey)
        where TEntity : BaseTenantEntity
        where TPrincipal : BaseTenantEntity
    {
        modelBuilder.Entity<TEntity>()
            .HasOne<TPrincipal>()
            .WithMany()
            .HasForeignKey(nameof(BaseTenantEntity.TenantId), foreignKey)
            .HasPrincipalKey(nameof(BaseTenantEntity.TenantId), nameof(BaseEntity.Id))
            .OnDelete(DeleteBehavior.Restrict);
    }


    private static void ConfigureIndexes(ModelBuilder modelBuilder)
    {
        // Platform / SaaS
        modelBuilder.Entity<Tenant>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Tenant>().HasIndex(x => x.Subdomain).IsUnique().HasFilter("[Subdomain] IS NOT NULL");
        modelBuilder.Entity<Tenant>().HasIndex(x => x.CustomDomain).IsUnique().HasFilter("[CustomDomain] IS NOT NULL");
        modelBuilder.Entity<InstitutionTypeDefinition>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ProductModule>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<Feature>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<SubscriptionPlan>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<ProductModuleFeature>().HasIndex(x => new { x.ProductModuleId, x.FeatureId }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<InstitutionTypeModule>().HasIndex(x => new { x.InstitutionTypeDefinitionId, x.ProductModuleId }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<PlanFeature>().HasIndex(x => new { x.SubscriptionPlanId, x.FeatureId }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<TenantDomain>().HasIndex(x => x.HostName).IsUnique();
        UniqueTenant<Campus>(modelBuilder, nameof(Campus.Code));
        UniqueTenant<TenantSetting>(modelBuilder, nameof(TenantSetting.Key));
        UniqueTenant<TenantTerminology>(modelBuilder, nameof(TenantTerminology.Key));
        UniqueTenant<TenantModule>(modelBuilder, nameof(TenantModule.ProductModuleId));
        UniqueTenant<TenantFeature>(modelBuilder, nameof(TenantFeature.FeatureId));
        UniqueTenant<SubscriptionInvoice>(modelBuilder, nameof(SubscriptionInvoice.InvoiceNumber));
        UniqueTenant<SubscriptionPayment>(modelBuilder, nameof(SubscriptionPayment.ClientRequestId));
        UniqueTenant<SubscriptionPayment>(modelBuilder, nameof(SubscriptionPayment.TransactionId));
        UniqueTenant<UsageStatistics>(modelBuilder, nameof(EduOS.Core.Entities.SaaS.UsageStatistics.PeriodStart), nameof(EduOS.Core.Entities.SaaS.UsageStatistics.PeriodEnd));
        modelBuilder.Entity<LegalDocument>().HasIndex(x => new { x.Code, x.Version }).IsUnique();
        modelBuilder.Entity<UserLegalAcceptance>().HasIndex(x => new { x.UserId, x.TenantId, x.LegalDocumentId }).IsUnique().HasFilter("[IsDeleted] = 0");

        // Identity / authorization
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<ApplicationUser>().HasIndex(x => x.PersonId).IsUnique().HasFilter("[PersonId] IS NOT NULL");
        modelBuilder.Entity<Permission>().HasIndex(x => x.Code).IsUnique();
        modelBuilder.Entity<RolePermission>().HasIndex(x => new { x.RoleId, x.PermissionId }).IsUnique().HasFilter("[IsDeleted] = 0");
        modelBuilder.Entity<UserPermission>().HasIndex(x => new { x.UserId, x.TenantId, x.PermissionId }).IsUnique().HasFilter("[IsDeleted] = 0");
        UniqueTenant<TenantMembership>(modelBuilder, nameof(TenantMembership.UserId));
        UniqueTenant<UserCampusAccess>(modelBuilder, nameof(UserCampusAccess.UserId), nameof(UserCampusAccess.CampusId));
        modelBuilder.Entity<TenantInvitation>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<TenantInvitation>(modelBuilder, nameof(TenantInvitation.TokenHash));
        modelBuilder.Entity<RefreshToken>().HasIndex(x => x.TokenHash).IsUnique();
        modelBuilder.Entity<TwoFactorAuth>().HasIndex(x => x.UserId).IsUnique();
        modelBuilder.Entity<TwoFactorRecoveryCode>().HasIndex(x => new { x.UserId, x.CodeHash }).IsUnique();

        // Global learner identity
        modelBuilder.Entity<Person>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<PersonIdentifier>().HasIndex(x => new { x.IdentifierType, x.LookupDigest }).IsUnique();
        modelBuilder.Entity<PersonAddress>().HasIndex(x => new { x.PersonId, x.AddressTypeCode }).HasFilter("[IsDeleted] = 0");
        UniqueTenant<StudentPersonLink>(modelBuilder, nameof(StudentPersonLink.PersonId), nameof(StudentPersonLink.StudentId));
        modelBuilder.Entity<LearnerConsentRequest>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<LearnerConsentRequest>().HasIndex(x => new { x.TenantId, x.PersonId, x.RequestedAt });
        modelBuilder.Entity<LearnerDataGrant>().HasIndex(x => new { x.TenantId, x.PersonId, x.StudentId, x.State });
        modelBuilder.Entity<LearnerIdentityAccessLog>().HasIndex(x => new { x.TenantId, x.PersonId, x.AccessedAt });

        // Students
        modelBuilder.Entity<Student>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<Student>(modelBuilder, nameof(Student.StudentCode));
        UniqueTenant<Student>(modelBuilder, new[] { nameof(Student.AdmissionApplicantId) }, "[AdmissionApplicantId] IS NOT NULL");
        UniqueTenant<Student>(modelBuilder, nameof(Student.PersonId));
        modelBuilder.Entity<Guardian>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<Guardian>(modelBuilder, nameof(Guardian.PersonId));
        UniqueTenant<StudentGuardian>(modelBuilder, nameof(StudentGuardian.StudentId), nameof(StudentGuardian.GuardianId));
        modelBuilder.Entity<StudentGuardian>().HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasFilter("[IsPrimary] = 1 AND [IsDeleted] = 0");
        UniqueTenant<StudentPromotionRecord>(modelBuilder, nameof(StudentPromotionRecord.ClientRequestId));
        UniqueTenant<StudentExitRecord>(modelBuilder, nameof(StudentExitRecord.ClientRequestId));
        modelBuilder.Entity<TransferRequest>().HasIndex(x => x.PublicId).IsUnique();
        modelBuilder.Entity<TransferRequest>().HasIndex(x => new { x.SourceTenantId, x.ClientRequestId }).IsUnique().HasFilter("[IsDeleted] = 0");
        UniqueTenant<TransferCertificate>(modelBuilder, nameof(TransferCertificate.ClientRequestId));
        UniqueTenant<TransferCertificate>(modelBuilder, nameof(TransferCertificate.CertificateNumber));

        // Academic
        UniqueTenant<AcademicYear>(modelBuilder, nameof(AcademicYear.Code));
        modelBuilder.Entity<AcademicTerm>().HasIndex(x => new { x.TenantId, x.AcademicYearId, x.Code })
            .IsUnique().HasFilter("[Code] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<AcademicYear>().HasIndex(x => new { x.TenantId, x.CampusId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<AcademicTerm>().HasIndex(x => new { x.TenantId, x.AcademicYearId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        UniqueTenant<AcademicDepartment>(modelBuilder, nameof(AcademicDepartment.Code));
        UniqueTenant<AcademicProgram>(modelBuilder, nameof(AcademicProgram.Code));
        UniqueTenant<AcademicLevel>(modelBuilder, nameof(AcademicLevel.AcademicProgramId), nameof(AcademicLevel.Code));
        UniqueTenant<AcademicTrack>(modelBuilder, nameof(AcademicTrack.Code));
        UniqueTenant<Medium>(modelBuilder, nameof(Medium.Code));
        UniqueTenant<Shift>(modelBuilder, nameof(Shift.Code));
        UniqueTenant<Subject>(modelBuilder, nameof(Subject.Code));
        UniqueTenant<AcademicCurriculum>(modelBuilder, nameof(AcademicCurriculum.Code));
        UniqueTenant<AcademicBatch>(modelBuilder, nameof(AcademicBatch.CampusId), nameof(AcademicBatch.AcademicYearId), nameof(AcademicBatch.Code));
        UniqueTenant<Room>(modelBuilder, nameof(Room.CampusId), nameof(Room.Code));
        UniqueTenant<ProgramCampus>(modelBuilder, nameof(ProgramCampus.AcademicProgramId), nameof(ProgramCampus.CampusId));
        UniqueTenant<CurriculumSubject>(modelBuilder, nameof(CurriculumSubject.AcademicCurriculumId), nameof(CurriculumSubject.AcademicLevelId), nameof(CurriculumSubject.SubjectId));
        UniqueTenant<SubjectPrerequisite>(modelBuilder, nameof(SubjectPrerequisite.AcademicCurriculumId), nameof(SubjectPrerequisite.SubjectId), nameof(SubjectPrerequisite.PrerequisiteSubjectId));
        UniqueTenant<SubjectOffering>(modelBuilder, nameof(SubjectOffering.AcademicBatchId), nameof(SubjectOffering.Code));
        UniqueTenant<StudentEnrollment>(modelBuilder, nameof(StudentEnrollment.ClientRequestId));
        modelBuilder.Entity<StudentEnrollment>().HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<StudentEnrollment>().HasIndex(x => new { x.TenantId, x.AcademicBatchId, x.RollNo })
            .IsUnique().HasFilter("[RollNo] <> ''");
        modelBuilder.Entity<StudentEnrollment>().HasIndex(x => new { x.TenantId, x.RegistrationNo })
            .IsUnique().HasFilter("[RegistrationNo] IS NOT NULL");
        UniqueTenant<StudentSubjectRegistration>(modelBuilder, nameof(StudentSubjectRegistration.StudentEnrollmentId), nameof(StudentSubjectRegistration.SubjectOfferingId));
        UniqueTenant<StudentSubjectRegistration>(modelBuilder, nameof(StudentSubjectRegistration.ClientRequestId));
        UniqueTenant<InstructorAssignment>(modelBuilder, nameof(InstructorAssignment.SubjectOfferingId), nameof(InstructorAssignment.EmployeeId));
        UniqueTenant<Substitution>(modelBuilder, nameof(Substitution.ClientRequestId));
        UniqueTenant<AttendanceSession>(modelBuilder, nameof(AttendanceSession.ClientRequestId));
        modelBuilder.Entity<AcademicCurriculum>().HasIndex(x => new { x.TenantId, x.AcademicProgramId, x.AcademicTrackId, x.MediumId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

        // Admission
        modelBuilder.Entity<AdmissionIntakeForm>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<AdmissionApplicant>(modelBuilder, nameof(AdmissionApplicant.ClientRequestId));
        UniqueTenant<AdmissionApplicant>(modelBuilder, nameof(AdmissionApplicant.ApplicationNumber));
        UniqueTenant<AdmissionApplicantFieldValue>(modelBuilder, nameof(AdmissionApplicantFieldValue.AdmissionApplicantId), nameof(AdmissionApplicantFieldValue.AdmissionFormFieldId));
        UniqueTenant<AdmissionResult>(modelBuilder, nameof(AdmissionResult.AdmissionTestId), nameof(AdmissionResult.AdmissionApplicantId));
        UniqueTenant<AdmissionDecision>(modelBuilder, nameof(AdmissionDecision.ClientRequestId));
        UniqueTenant<AdmissionPayment>(modelBuilder, nameof(AdmissionPayment.ClientRequestId));
        UniqueTenant<AdmissionPayment>(modelBuilder, nameof(AdmissionPayment.ReceiptNumber));

        // Assessment / result
        UniqueTenant<GradeScheme>(modelBuilder, nameof(GradeScheme.Code));
        UniqueTenant<GradeRule>(modelBuilder, nameof(GradeRule.GradeSchemeId), nameof(GradeRule.GradeLetter));
        UniqueTenant<Assessment>(modelBuilder, nameof(Assessment.Code), nameof(Assessment.VersionNo));
        UniqueTenant<AssessmentSubject>(modelBuilder, nameof(AssessmentSubject.AssessmentId), nameof(AssessmentSubject.SubjectOfferingId));
        UniqueTenant<AssessmentComponent>(modelBuilder, nameof(AssessmentComponent.AssessmentSubjectId), nameof(AssessmentComponent.Code));
        UniqueTenant<StudentAssessmentMark>(modelBuilder, nameof(StudentAssessmentMark.AssessmentSubjectId), nameof(StudentAssessmentMark.StudentSubjectRegistrationId));
        UniqueTenant<StudentAssessmentComponentMark>(modelBuilder, nameof(StudentAssessmentComponentMark.AssessmentComponentId), nameof(StudentAssessmentComponentMark.StudentAssessmentMarkId));
        UniqueTenant<ResultPublication>(modelBuilder, nameof(ResultPublication.AssessmentId), nameof(ResultPublication.AcademicBatchId), nameof(ResultPublication.VersionNo));
        UniqueTenant<StudentResultSummary>(modelBuilder, nameof(StudentResultSummary.ResultPublicationId), nameof(StudentResultSummary.StudentEnrollmentId));
        UniqueTenant<CertificateTemplate>(modelBuilder, nameof(CertificateTemplate.Code));
        UniqueTenant<CertificateIssue>(modelBuilder, nameof(CertificateIssue.CertificateNumber));
        UniqueTenant<TranscriptIssue>(modelBuilder, nameof(TranscriptIssue.TranscriptNumber));

        // Attendance / HR
        UniqueTenant<StudentAttendance>(modelBuilder, nameof(StudentAttendance.AttendanceSessionId), nameof(StudentAttendance.StudentEnrollmentId));
        UniqueTenant<EmployeeAttendance>(modelBuilder, nameof(EmployeeAttendance.EmployeeId), nameof(EmployeeAttendance.AttendanceDate));
        UniqueTenant<EmployeeLeaveEntitlement>(modelBuilder, nameof(EmployeeLeaveEntitlement.EmployeeId), nameof(EmployeeLeaveEntitlement.LeaveTypeId), nameof(EmployeeLeaveEntitlement.Year));
        UniqueTenant<EmployeeLeaveApplication>(modelBuilder, nameof(EmployeeLeaveApplication.ClientRequestId));
        UniqueTenant<StudentLeaveApplication>(modelBuilder, nameof(StudentLeaveApplication.ClientRequestId));
        UniqueTenant<OrganizationUnit>(modelBuilder, nameof(OrganizationUnit.Code));
        UniqueTenant<Designation>(modelBuilder, nameof(Designation.Code));
        UniqueTenant<WorkShift>(modelBuilder, nameof(WorkShift.Code));
        UniqueTenant<Employee>(modelBuilder, nameof(Employee.EmployeeCode));
        UniqueTenant<Employee>(modelBuilder, nameof(Employee.PersonId));
        modelBuilder.Entity<EmployeeBankAccount>().HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique().HasFilter("[IsPrimary] = 1 AND [IsActive] = 1 AND [IsDeleted] = 0");
        UniqueTenant<EmployeeCampusAssignment>(modelBuilder, nameof(EmployeeCampusAssignment.EmployeeId), nameof(EmployeeCampusAssignment.CampusId),
            "[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<EmployeeCampusAssignment>().HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsPrimary] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<EmployeeShiftAssignment>().HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<EmployeeAssignmentHistory>().HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<SalaryStructure>().HasIndex(x => new { x.TenantId, x.EmployeeId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

        // Student finance / accounting
        UniqueTenant<FeeHead>(modelBuilder, nameof(FeeHead.Code));
        UniqueTenant<FeeStructureLine>(modelBuilder, nameof(FeeStructureLine.FeeStructureId), nameof(FeeStructureLine.FeeHeadId), nameof(FeeStructureLine.Frequency));
        modelBuilder.Entity<StudentInvoice>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<StudentInvoice>(modelBuilder, nameof(StudentInvoice.InvoiceNumber));
        UniqueTenant<StudentInvoice>(modelBuilder, nameof(StudentInvoice.ClientRequestId));
        modelBuilder.Entity<StudentInvoice>().HasIndex(x => new { x.TenantId, x.State, x.DueDate });
        modelBuilder.Entity<StudentPayment>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<StudentPayment>(modelBuilder, nameof(StudentPayment.ReceiptNumber));
        UniqueTenant<StudentPayment>(modelBuilder, nameof(StudentPayment.ClientRequestId));
        UniqueTenant<PaymentAllocation>(modelBuilder, nameof(PaymentAllocation.StudentPaymentId), nameof(PaymentAllocation.StudentInvoiceId));
        UniqueTenant<Refund>(modelBuilder, nameof(Refund.ClientRequestId));
        UniqueTenant<Account>(modelBuilder, nameof(Account.Code));
        modelBuilder.Entity<Account>().HasIndex(x => new { x.TenantId, x.ParentAccountId, x.IsActive });
        UniqueTenant<NumberSeries>(modelBuilder, nameof(EduOS.Core.Entities.System.NumberSeries.Key));
        modelBuilder.Entity<Journal>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<Journal>(modelBuilder, nameof(Journal.JournalNumber));
        modelBuilder.Entity<Journal>().HasIndex(x => new { x.TenantId, x.IdempotencyKey })
            .IsUnique().HasFilter("[IdempotencyKey] IS NOT NULL AND [IsDeleted] = 0");
        UniqueTenant<JournalLine>(modelBuilder, nameof(JournalLine.JournalId), nameof(JournalLine.LineNo));
        modelBuilder.Entity<Journal>().HasIndex(x => new { x.TenantId, x.JournalDate, x.State });
        modelBuilder.Entity<AccountingPeriod>().HasIndex(x => new { x.TenantId, x.StartDate, x.EndDate });

        // Payroll
        UniqueTenant<SalaryComponent>(modelBuilder, nameof(SalaryComponent.Code));
        UniqueTenant<SalaryStructureLine>(modelBuilder, nameof(SalaryStructureLine.SalaryStructureId), nameof(SalaryStructureLine.SalaryComponentId));
        modelBuilder.Entity<PayrollRun>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<PayrollRun>(modelBuilder, nameof(PayrollRun.RunNumber));
        UniqueTenant<PayrollRun>(modelBuilder, nameof(PayrollRun.ClientRequestId));
        UniqueTenant<PayrollEmployee>(modelBuilder, nameof(PayrollEmployee.PayrollRunId), nameof(PayrollEmployee.EmployeeId));
        UniqueTenant<PayrollLine>(modelBuilder, nameof(PayrollLine.PayrollEmployeeId), nameof(PayrollLine.SalaryComponentId));
        UniqueTenant<LoanAdvanceRecovery>(modelBuilder, nameof(LoanAdvanceRecovery.ClientRequestId));
        UniqueTenant<PayrollPayment>(modelBuilder, nameof(PayrollPayment.ClientRequestId));

        // Library
        UniqueTenant<BookCategory>(modelBuilder, nameof(BookCategory.Code));
        UniqueTenant<LibraryBranch>(modelBuilder, nameof(LibraryBranch.Code));
        UniqueTenant<BookCopy>(modelBuilder, nameof(BookCopy.AccessionNumber));
        UniqueTenant<BookCopy>(modelBuilder, new[] { nameof(BookCopy.Barcode) }, "[Barcode] IS NOT NULL");
        UniqueTenant<BookIssue>(modelBuilder, nameof(BookIssue.ClientRequestId));
        UniqueTenant<BookReservation>(modelBuilder, nameof(BookReservation.ClientRequestId));

        // Hostel / transport
        UniqueTenant<Hostel>(modelBuilder, nameof(Hostel.Code));
        UniqueTenant<HostelRoom>(modelBuilder, nameof(HostelRoom.HostelId), nameof(HostelRoom.RoomNumber));
        UniqueTenant<HostelBed>(modelBuilder, nameof(HostelBed.HostelRoomId), nameof(HostelBed.BedNumber));
        UniqueTenant<StudentHostelAllocation>(modelBuilder, nameof(StudentHostelAllocation.ClientRequestId));
        modelBuilder.Entity<StudentHostelAllocation>().HasIndex(x => new { x.TenantId, x.HostelBedId })
            .IsUnique().HasFilter("[EndDate] IS NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<StudentHostelAllocation>().HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasFilter("[EndDate] IS NULL AND [IsDeleted] = 0");
        UniqueTenant<Route>(modelBuilder, nameof(Route.Code));
        UniqueTenant<RouteStop>(modelBuilder, nameof(RouteStop.RouteId), nameof(RouteStop.SequenceNo));
        UniqueTenant<Vehicle>(modelBuilder, nameof(Vehicle.VehicleNumber));
        UniqueTenant<StudentTransport>(modelBuilder, nameof(StudentTransport.ClientRequestId));
        modelBuilder.Entity<StudentTransport>().HasIndex(x => new { x.TenantId, x.StudentId })
            .IsUnique().HasFilter("[EndDate] IS NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<RouteVehicleAssignment>().HasIndex(x => new { x.TenantId, x.RouteId, x.VehicleId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<RouteVehicleAssignment>().HasIndex(x => new { x.TenantId, x.VehicleId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<VehicleDriverAssignment>().HasIndex(x => new { x.TenantId, x.VehicleId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");
        modelBuilder.Entity<VehicleDriverAssignment>().HasIndex(x => new { x.TenantId, x.TransportDriverId })
            .IsUnique().HasFilter("[IsCurrent] = 1 AND [IsDeleted] = 0");

        // LMS
        UniqueTenant<Course>(modelBuilder, nameof(Course.Code));
        UniqueTenant<CourseEnrollment>(modelBuilder, nameof(CourseEnrollment.ClientRequestId));
        modelBuilder.Entity<CourseEnrollment>().HasIndex(x => new { x.TenantId, x.CourseId, x.StudentEnrollmentId })
            .IsUnique().HasFilter("[StudentEnrollmentId] IS NOT NULL AND [IsDeleted] = 0");
        modelBuilder.Entity<CourseEnrollment>().HasIndex(x => new { x.TenantId, x.CourseId, x.StudentId })
            .IsUnique().HasFilter("[StudentEnrollmentId] IS NULL AND [IsDeleted] = 0");
        UniqueTenant<LessonProgress>(modelBuilder, nameof(LessonProgress.CourseEnrollmentId), nameof(LessonProgress.LessonId));
        UniqueTenant<QuizAttempt>(modelBuilder, nameof(QuizAttempt.ClientRequestId));
        UniqueTenant<QuizAnswer>(modelBuilder, nameof(QuizAnswer.QuizAttemptId), nameof(QuizAnswer.QuizQuestionId));
        UniqueTenant<QuizAnswerOption>(modelBuilder, nameof(QuizAnswerOption.QuizAnswerId), nameof(QuizAnswerOption.QuizOptionId));

        // Communication / files / system
        UniqueTenant<NoticeReadReceipt>(modelBuilder, nameof(NoticeReadReceipt.NoticeId), nameof(NoticeReadReceipt.UserId));
        UniqueTenant<NoticeAudience>(modelBuilder, nameof(NoticeAudience.NoticeId), nameof(NoticeAudience.AudienceTypeCode), nameof(NoticeAudience.CampusId), nameof(NoticeAudience.AcademicProgramId), nameof(NoticeAudience.AcademicBatchId), nameof(NoticeAudience.UserId));
        UniqueTenant<MessageThreadParticipant>(modelBuilder, nameof(MessageThreadParticipant.MessageThreadId), nameof(MessageThreadParticipant.UserId));
        UniqueTenant<NotificationPreference>(modelBuilder, nameof(NotificationPreference.UserId), nameof(NotificationPreference.Channel), nameof(NotificationPreference.EventCode));
        UniqueTenant<CommunicationDelivery>(modelBuilder, nameof(CommunicationDelivery.IdempotencyKey));
        modelBuilder.Entity<FileAsset>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<FileAsset>(modelBuilder, nameof(FileAsset.StorageProvider), nameof(FileAsset.StorageKey));
        UniqueTenant<DocumentTemplate>(modelBuilder, nameof(DocumentTemplate.Code));
        UniqueTenant<CustomFieldDefinition>(modelBuilder, nameof(CustomFieldDefinition.EntityType), nameof(CustomFieldDefinition.FieldKey));
        UniqueTenant<CustomFieldOption>(modelBuilder, nameof(CustomFieldOption.CustomFieldDefinitionId), nameof(CustomFieldOption.Value));
        UniqueTenant<IdempotencyRecord>(modelBuilder, nameof(IdempotencyRecord.Scope), nameof(IdempotencyRecord.RequestId));
        modelBuilder.Entity<OutboxMessage>().HasIndex(x => x.EventId).IsUnique().HasFilter("[IsDeleted] = 0");
        UniqueTenant<ApiCredential>(modelBuilder, nameof(ApiCredential.KeyPrefix));
        modelBuilder.Entity<WebhookDelivery>().HasIndex(x => new { x.TenantId, x.State, x.NextAttemptAt });

        // Inventory / asset
        UniqueTenant<InventoryLocation>(modelBuilder, nameof(InventoryLocation.Code));
        UniqueTenant<InventoryItem>(modelBuilder, nameof(InventoryItem.Code));
        modelBuilder.Entity<InventoryMovement>().HasIndex(x => x.PublicId).IsUnique();
        UniqueTenant<InventoryMovement>(modelBuilder, nameof(InventoryMovement.MovementNumber));
        UniqueTenant<InventoryMovement>(modelBuilder, nameof(InventoryMovement.ClientRequestId));
        UniqueTenant<InventoryMovementLine>(modelBuilder, nameof(InventoryMovementLine.InventoryMovementId), nameof(InventoryMovementLine.LineNo));
        UniqueTenant<AssetCategory>(modelBuilder, nameof(AssetCategory.Code));
        UniqueTenant<Asset>(modelBuilder, nameof(Asset.AssetTag));
        UniqueTenant<AssetAssignment>(modelBuilder, nameof(AssetAssignment.ClientRequestId));
    }

    private static void UniqueTenant<TEntity>(ModelBuilder modelBuilder, params string[] propertyNames)
        where TEntity : BaseTenantEntity =>
        UniqueTenant<TEntity>(modelBuilder, propertyNames, null);

    private static void UniqueTenant<TEntity>(ModelBuilder modelBuilder, string[] propertyNames, string? filter)
        where TEntity : BaseTenantEntity
    {
        var names = new[] { nameof(BaseTenantEntity.TenantId) }.Concat(propertyNames).ToArray();
        var index = modelBuilder.Entity<TEntity>().HasIndex(names).IsUnique();

        if (!string.IsNullOrWhiteSpace(filter))
            index.HasFilter(filter);
    }


    private static void ConfigureCheckConstraints(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AcademicYear>().ToTable(t =>
            t.HasCheckConstraint("CK_AcademicYear_DateRange", "[EndDate] >= [StartDate]"));
        modelBuilder.Entity<AcademicTerm>().ToTable(t =>
            t.HasCheckConstraint("CK_AcademicTerm_DateRange", "[EndDate] >= [StartDate]"));
        modelBuilder.Entity<AcademicCalendarEvent>().ToTable(t =>
            t.HasCheckConstraint("CK_AcademicCalendarEvent_DateRange", "[EndDate] >= [StartDate]"));
        modelBuilder.Entity<AcademicBatch>().ToTable(t =>
            t.HasCheckConstraint("CK_AcademicBatch_DateRange", "[EndDate] IS NULL OR [StartDate] IS NULL OR [EndDate] >= [StartDate]"));
        modelBuilder.Entity<StudentEnrollment>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentEnrollment_DateRange", "[EndDate] IS NULL OR [EndDate] >= [EnrollmentDate]"));
        modelBuilder.Entity<Assessment>().ToTable(t =>
            t.HasCheckConstraint("CK_Assessment_DateRange", "[EndDate] >= [StartDate]"));
        modelBuilder.Entity<AccountingPeriod>().ToTable(t =>
            t.HasCheckConstraint("CK_AccountingPeriod_DateRange", "[EndDate] >= [StartDate]"));
        modelBuilder.Entity<FeeStructure>().ToTable(t =>
            t.HasCheckConstraint("CK_FeeStructure_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<AcademicCurriculum>().ToTable(t =>
            t.HasCheckConstraint("CK_AcademicCurriculum_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<EmployeeCampusAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_EmployeeCampusAssignment_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<EmployeeShiftAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_EmployeeShiftAssignment_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<EmployeeAssignmentHistory>().ToTable(t =>
            t.HasCheckConstraint("CK_EmployeeAssignmentHistory_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<EmployeeLeaveApplication>().ToTable(t =>
            t.HasCheckConstraint("CK_EmployeeLeaveApplication_DateRange", "[ToDate] >= [FromDate] AND [TotalDays] > 0"));
        modelBuilder.Entity<StudentLeaveApplication>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentLeaveApplication_DateRange", "[ToDate] >= [FromDate]"));
        modelBuilder.Entity<SalaryStructure>().ToTable(t =>
            t.HasCheckConstraint("CK_SalaryStructure_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<RouteVehicleAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_RouteVehicleAssignment_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<VehicleDriverAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_VehicleDriverAssignment_DateRange", "[EffectiveTo] IS NULL OR [EffectiveTo] >= [EffectiveFrom]"));
        modelBuilder.Entity<StudentHostelAllocation>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentHostelAllocation_DateRange", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
        modelBuilder.Entity<StudentTransport>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentTransport_DateRange", "[EndDate] IS NULL OR [EndDate] >= [StartDate]"));
        modelBuilder.Entity<AssetAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_AssetAssignment_DateRange", "[ReturnedOn] IS NULL OR [ReturnedOn] >= [AssignedOn]"));
        modelBuilder.Entity<BookIssue>().ToTable(t =>
            t.HasCheckConstraint("CK_BookIssue_DateRange", "[DueDate] >= [IssueDate] AND ([ReturnDate] IS NULL OR [ReturnDate] >= [IssueDate])"));
        modelBuilder.Entity<TenantSubscription>().ToTable(t =>
            t.HasCheckConstraint("CK_TenantSubscription_DateRange", "[EndsAt] >= [StartsAt]"));

        modelBuilder.Entity<JournalLine>().ToTable(t =>
            t.HasCheckConstraint("CK_JournalLine_DebitCredit",
                "(([DebitAmount] > 0 AND [CreditAmount] = 0) OR ([CreditAmount] > 0 AND [DebitAmount] = 0))"));
        modelBuilder.Entity<GradeRule>().ToTable(t =>
            t.HasCheckConstraint("CK_GradeRule_Range", "[MaxMarks] >= [MinMarks] AND [MinMarks] >= 0"));
        modelBuilder.Entity<AssessmentSubject>().ToTable(t =>
            t.HasCheckConstraint("CK_AssessmentSubject_Marks", "[FullMarks] > 0 AND [PassMarks] >= 0 AND [PassMarks] <= [FullMarks] AND [Weightage] >= 0"));
        modelBuilder.Entity<AssessmentComponent>().ToTable(t =>
            t.HasCheckConstraint("CK_AssessmentComponent_Marks", "[FullMarks] > 0 AND [PassMarks] >= 0 AND [PassMarks] <= [FullMarks] AND [Weightage] >= 0"));
        modelBuilder.Entity<SubscriptionInvoice>().ToTable(t =>
            t.HasCheckConstraint("CK_SubscriptionInvoice_Amounts",
                "[DueDate] >= [InvoiceDate] AND [Subtotal] >= 0 AND [TaxAmount] >= 0 AND [TotalAmount] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [TotalAmount] AND [DueAmount] = [TotalAmount] - [PaidAmount]"));
        modelBuilder.Entity<SubscriptionInvoiceLine>().ToTable(t =>
            t.HasCheckConstraint("CK_SubscriptionInvoiceLine_Amounts", "[Quantity] > 0 AND [UnitPrice] >= 0 AND [Amount] >= 0"));
        modelBuilder.Entity<SubscriptionPayment>().ToTable(t =>
            t.HasCheckConstraint("CK_SubscriptionPayment_Amount", "[Amount] > 0"));
        modelBuilder.Entity<FeeStructureLine>().ToTable(t =>
            t.HasCheckConstraint("CK_FeeStructureLine_AmountDueDay",
                "[Amount] >= 0 AND ([DueDayOfMonth] IS NULL OR ([DueDayOfMonth] BETWEEN 1 AND 31))"));
        modelBuilder.Entity<DiscountRule>().ToTable(t =>
            t.HasCheckConstraint("CK_DiscountRule_Value",
                "[Value] >= 0 AND ([IsPercentage] = 0 OR [Value] <= 100) AND ([EffectiveTo] IS NULL OR [EffectiveFrom] IS NULL OR [EffectiveTo] >= [EffectiveFrom])"));
        modelBuilder.Entity<FineRule>().ToTable(t =>
            t.HasCheckConstraint("CK_FineRule_Amount", "[Amount] >= 0 AND [GraceDays] >= 0"));
        modelBuilder.Entity<StudentFine>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentFine_Amount", "[Amount] >= 0"));
        modelBuilder.Entity<StudentInvoice>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentInvoice_Amounts",
                "[Subtotal] >= 0 AND [DiscountAmount] >= 0 AND [FineAmount] >= 0 AND [TotalAmount] >= 0 AND [PaidAmount] >= 0 AND [PaidAmount] <= [TotalAmount] AND [DueAmount] = [TotalAmount] - [PaidAmount]"));
        modelBuilder.Entity<StudentInvoiceLine>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentInvoiceLine_Amounts",
                "[Amount] >= 0 AND [DiscountAmount] >= 0 AND [FineAmount] >= 0 AND [NetAmount] >= 0"));
        modelBuilder.Entity<StudentPayment>().ToTable(t =>
            t.HasCheckConstraint("CK_StudentPayment_Amount", "[Amount] > 0"));
        modelBuilder.Entity<PaymentAllocation>().ToTable(t =>
            t.HasCheckConstraint("CK_PaymentAllocation_Amount", "[Amount] > 0"));
        modelBuilder.Entity<Refund>().ToTable(t =>
            t.HasCheckConstraint("CK_Refund_Amount", "[Amount] > 0"));
        modelBuilder.Entity<RefundAllocation>().ToTable(t =>
            t.HasCheckConstraint("CK_RefundAllocation_Amount", "[Amount] > 0"));
        modelBuilder.Entity<AdmissionPayment>().ToTable(t =>
            t.HasCheckConstraint("CK_AdmissionPayment_Amount", "[Amount] > 0"));
        modelBuilder.Entity<PayrollPayment>().ToTable(t =>
            t.HasCheckConstraint("CK_PayrollPayment_Amount", "[Amount] > 0"));
        modelBuilder.Entity<SalaryStructureLine>().ToTable(t =>
            t.HasCheckConstraint("CK_SalaryStructureLine_Amount",
                "[Amount] >= 0 AND ([Percentage] IS NULL OR ([Percentage] >= 0 AND [Percentage] <= 100))"));
        modelBuilder.Entity<PayrollEmployee>().ToTable(t =>
            t.HasCheckConstraint("CK_PayrollEmployee_Amounts",
                "[GrossAmount] >= 0 AND [DeductionAmount] >= 0 AND [NetAmount] >= 0 AND [NetAmount] = [GrossAmount] - [DeductionAmount]"));
        modelBuilder.Entity<PayrollLine>().ToTable(t =>
            t.HasCheckConstraint("CK_PayrollLine_Amount", "[Amount] >= 0"));
        modelBuilder.Entity<Bonus>().ToTable(t =>
            t.HasCheckConstraint("CK_Bonus_Amount", "[Amount] >= 0"));
        modelBuilder.Entity<LoanAdvance>().ToTable(t =>
            t.HasCheckConstraint("CK_LoanAdvance_Amounts",
                "[PrincipalAmount] >= 0 AND [OutstandingAmount] >= 0 AND [InstallmentAmount] >= 0 AND [OutstandingAmount] <= [PrincipalAmount]"));
        modelBuilder.Entity<LoanAdvanceRecovery>().ToTable(t =>
            t.HasCheckConstraint("CK_LoanAdvanceRecovery_Amount", "[Amount] > 0"));
        modelBuilder.Entity<InventoryMovementLine>().ToTable(t =>
            t.HasCheckConstraint("CK_InventoryMovementLine_QuantityCost", "[Quantity] > 0 AND [UnitCost] >= 0"));
        modelBuilder.Entity<Asset>().ToTable(t =>
            t.HasCheckConstraint("CK_Asset_PurchaseCost", "[PurchaseCost] IS NULL OR [PurchaseCost] >= 0"));
        modelBuilder.Entity<AssetMaintenance>().ToTable(t =>
            t.HasCheckConstraint("CK_AssetMaintenance_Cost", "[Cost] >= 0"));
        modelBuilder.Entity<NumberSeries>().ToTable(t =>
            t.HasCheckConstraint("CK_NumberSeries_Value", "[NextValue] > 0 AND [Padding] >= 0"));
        modelBuilder.Entity<AssetAssignment>().ToTable(t =>
            t.HasCheckConstraint("CK_AssetAssignment_OneAssignee",
                "(CASE WHEN [EmployeeId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [StudentId] IS NULL THEN 0 ELSE 1 END + CASE WHEN [OrganizationUnitId] IS NULL THEN 0 ELSE 1 END) = 1"));
    }


    private static void ConfigureAuditLog(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.Property(x => x.OldValue).HasColumnType("nvarchar(max)");
            entity.Property(x => x.NewValue).HasColumnType("nvarchar(max)");
            entity.HasIndex(x => new { x.TenantId, x.UserId, x.OccurredAt });
            entity.HasIndex(x => new { x.TenantId, x.EntityName, x.EntityId, x.OccurredAt });
        });
    }

    private static void ConfigureSpecialMappings(ModelBuilder modelBuilder)
    {
        // ParentAccountId is the authoritative hierarchy FK. ParentId is a legacy duplicate and must not become a second source of truth.
        modelBuilder.Entity<Account>().Ignore(x => x.ParentId);

        modelBuilder.Entity<TenantSetting>().Property(x => x.Value).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<LegalDocument>().Property(x => x.Content).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<CertificateTemplate>().Property(x => x.HtmlTemplate).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<DocumentTemplate>().Property(x => x.TemplateContent).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<Message>().Property(x => x.Body).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<CommunicationDelivery>().Property(x => x.Body).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<OutboxMessage>().Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
        modelBuilder.Entity<WebhookDelivery>().Property(x => x.PayloadJson).HasColumnType("nvarchar(max)");
    }

    private static void ApplyDateTimeType(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if ((property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                    && property.GetColumnType() == null)
                {
                    property.SetColumnType("datetime2");
                }
            }
        }
    }

    private static void DisableCascadeDelete(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
        }
    }

    #endregion

    #region SaveChanges / Audit / Tenant Safety

    public override int SaveChanges()
    {
        return SaveChangesAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        if (!acceptAllChangesOnSuccess)
            throw new NotSupportedException("EduOSDbContext requires acceptAllChangesOnSuccess=true so business data and audit data remain consistent.");

        return SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return SaveChangesWithAuditAsync(cancellationToken);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        if (!acceptAllChangesOnSuccess)
            throw new NotSupportedException("EduOSDbContext requires acceptAllChangesOnSuccess=true so business data and audit data remain consistent.");

        return SaveChangesWithAuditAsync(cancellationToken);
    }

    private async Task<int> SaveChangesWithAuditAsync(CancellationToken cancellationToken)
    {
        if (_savingAudit)
            return await base.SaveChangesAsync(true, cancellationToken);

        if (!ChangeTracker.HasChanges())
            return 0;

        ResolveContext();

        if (Database.CurrentTransaction != null)
            return await SaveChangesCoreAsync(cancellationToken);

        var strategy = Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
            try
            {
                var result = await SaveChangesCoreAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }

    private async Task<int> SaveChangesCoreAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var entries = ChangeTracker.Entries<BaseEntity>()
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        var identityEntries = ChangeTracker.Entries()
            .Where(x => x.Entity is ApplicationUser or ApplicationRole)
            .Where(x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        ValidateTenantBoundaries(entries);
        ValidateIdentityTenantBoundary(identityEntries);
        ValidateImmutableHistory(entries);
        await ValidateAggregateImmutabilityAsync(entries, cancellationToken);

        var pendingAudits = new List<PendingAudit>();

        foreach (var entry in entries)
        {
            if (entry.Entity is AuditLog)
                continue;

            var action = GetAuditAction(entry.State);
            if (string.IsNullOrWhiteSpace(action))
                continue;

            ApplyAuditFields(entry, now, action);
            var audit = CreateAudit(entry, action, now);
            if (audit != null)
                pendingAudits.Add(new PendingAudit(entry, action, audit));
        }

        foreach (var entry in identityEntries)
        {
            var action = GetAuditAction(entry.State);
            if (string.IsNullOrWhiteSpace(action))
                continue;

            ApplyIdentityAuditFields(entry, now, action);
            var audit = CreateAudit(entry, action, now);
            if (audit != null)
                pendingAudits.Add(new PendingAudit(entry, action, audit));
        }

        var affectedRows = await base.SaveChangesAsync(true, cancellationToken);

        if (pendingAudits.Count == 0)
            return affectedRows;

        foreach (var pending in pendingAudits)
        {
            pending.Log.EntityId = GetEntityId(pending.Entry);

            // Added keys may be database-generated. Refresh the create payload after the first save.
            if (pending.Action == "Create" && !SuppressAuditPayload(pending.Entry.Entity))
                pending.Log.NewValue = SerializeCurrentValues(pending.Entry);
        }

        _savingAudit = true;
        try
        {
            await AuditLogs.AddRangeAsync(pendingAudits.Select(x => x.Log), cancellationToken);
            await base.SaveChangesAsync(true, cancellationToken);
        }
        finally
        {
            _savingAudit = false;
        }

        return affectedRows;
    }

    private static string GetAuditAction(EntityState state)
    {
        return state switch
        {
            EntityState.Added => "Create",
            EntityState.Modified => "Update",
            EntityState.Deleted => "Delete",
            _ => string.Empty
        };
    }

    private void ApplyIdentityAuditFields(EntityEntry entry, DateTime now, string action)
    {
        switch (entry.Entity)
        {
            case ApplicationUser user:
                if (action == "Create")
                {
                    user.CreatedAt = now;
                }
                else
                {
                    user.UpdatedAt = now;
                }

                if (action == "Delete")
                {
                    entry.State = EntityState.Modified;
                    user.IsDeleted = true;
                    user.UpdatedAt = now;
                }
                break;

            case ApplicationRole role:
                if (action == "Create")
                {
                    role.CreatedAt = now;
                }
                else
                {
                    role.UpdatedAt = now;
                }

                if (action == "Delete")
                {
                    entry.State = EntityState.Modified;
                    role.IsDeleted = true;
                    role.UpdatedAt = now;
                }
                break;
        }
    }

    private void ApplyAuditFields(EntityEntry<BaseEntity> entry, DateTime now, string action)
    {
        if (action == "Create")
        {
            entry.Entity.CreatedAt = now;
            entry.Entity.CreatedBy = UserId;

            if (entry.Entity is ITenantScopedEntity tenantEntity && tenantEntity.TenantId == 0)
            {
                tenantEntity.TenantId = TenantId
                    ?? throw new InvalidOperationException("Tenant-scoped data requires a trusted tenant context or an explicit system tenant scope.");
            }

            return;
        }

        if (action == "Delete")
        {
            if (IsHardHistoryEntity(entry.Entity))
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is historical/transactional data and cannot be deleted. Use cancellation, reversal, expiry, or a new version.");

            entry.State = EntityState.Modified;
            entry.Entity.IsDeleted = true;
            entry.Entity.DeletedAt = now;
            entry.Entity.DeletedBy = UserId;
        }

        entry.Entity.UpdatedAt = now;
        entry.Entity.UpdatedBy = UserId;
    }

    private void ValidateTenantBoundaries(IEnumerable<EntityEntry<BaseEntity>> entries)
    {
        var requestTenantId = TenantId;
        var user = _httpContextAccessor?.HttpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated == true;
        var isPlatformAdmin = user?.IsInRole("SuperAdmin") == true;

        foreach (var entry in entries)
        {
            if (entry.Entity is not ITenantScopedEntity tenantEntity)
                continue;

            if (IsAuthorizedLearnerConsentWrite(entry, tenantEntity))
                continue;

            if (entry.State == EntityState.Modified)
            {
                var originalTenantId = entry.OriginalValues[nameof(ITenantScopedEntity.TenantId)];
                if (originalTenantId is long original && original > 0 && original != tenantEntity.TenantId)
                    throw new UnauthorizedAccessException("TenantId cannot be changed on an existing tenant-owned record.");
            }

            if (requestTenantId.HasValue)
            {
                if (entry.State == EntityState.Added && tenantEntity.TenantId == 0)
                    tenantEntity.TenantId = requestTenantId.Value;

                if (tenantEntity.TenantId != requestTenantId.Value)
                    throw new UnauthorizedAccessException("Tenant boundary violation. The record belongs to another tenant.");

                continue;
            }

            if (isAuthenticated && !isPlatformAdmin)
                throw new UnauthorizedAccessException("Tenant context is required for tenant-owned data.");

            if (tenantEntity.TenantId <= 0)
                throw new InvalidOperationException("Tenant-owned data requires an explicit TenantId.");
        }
    }

    private void ValidateIdentityTenantBoundary(IEnumerable<EntityEntry> entries)
    {
        var requestTenantId = TenantId;
        var user = _httpContextAccessor?.HttpContext?.User;
        var isAuthenticated = user?.Identity?.IsAuthenticated == true;
        var isPlatformAdmin = user?.IsInRole("SuperAdmin") == true;

        foreach (var entry in entries)
        {
            if (entry.Entity is not ApplicationRole role)
                continue;

            if (entry.State == EntityState.Modified)
            {
                var originalValue = entry.OriginalValues[nameof(ApplicationRole.TenantId)];
                var originalTenantId = originalValue == null ? (long?)null : Convert.ToInt64(originalValue);
                if (originalTenantId != role.TenantId)
                    throw new UnauthorizedAccessException("Role TenantId cannot be changed after the role is created.");
            }

            if (role.TenantId.HasValue)
            {
                if (requestTenantId.HasValue && role.TenantId.Value != requestTenantId.Value && !isPlatformAdmin)
                    throw new UnauthorizedAccessException("Tenant role boundary violation.");

                if (!requestTenantId.HasValue && isAuthenticated && !isPlatformAdmin)
                    throw new UnauthorizedAccessException("Tenant context is required to change a tenant role.");
            }
            else if (isAuthenticated && !isPlatformAdmin)
            {
                throw new UnauthorizedAccessException("Only platform administration may change global roles.");
            }
        }
    }

    private static void ValidateImmutableHistory(IEnumerable<EntityEntry<BaseEntity>> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.State is not (EntityState.Modified or EntityState.Deleted))
                continue;

            if (entry.Entity is AuditLog
                or LearnerIdentityAccessLog
                or StudentPromotionRecord
                or StudentExitRecord
                or StudentStatusHistory
                or PersonMergeRecord
                or StudentAttendanceAdjustment
                or EmployeeAttendanceAdjustment
                or PaymentAllocation
                or RefundAllocation
                or LoanAdvanceRecovery)
            {
                throw new InvalidOperationException($"{entry.Entity.GetType().Name} is append-only and cannot be updated or deleted.");
            }

            if (entry.Entity is Journal && OriginalStateEquals(entry, nameof(Journal.State), "Posted"))
                throw new InvalidOperationException("A posted journal is immutable. Create a reversal journal.");

            if (entry.Entity is PayrollRun && OriginalStateEquals(entry, nameof(PayrollRun.State), "Posted"))
                throw new InvalidOperationException("A posted payroll run is immutable. Correct it through controlled reversal/amendment.");

            if (entry.Entity is ResultPublication && OriginalStateEquals(entry, nameof(ResultPublication.State), "Published"))
                throw new InvalidOperationException("A published result version is immutable. Publish a controlled new version.");

            if (entry.Entity is InventoryMovement
                && string.Equals(
                    entry.OriginalValues[nameof(InventoryMovement.StateCode)]?.ToString(),
                    "Posted",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("A posted inventory movement is immutable. Create a reversal movement.");
            }

            if (entry.Entity is StudentPayment && OriginalStateEquals(entry, nameof(StudentPayment.State), "Successful"))
                throw new InvalidOperationException("A successful student payment is immutable. Use refund/allocation transactions.");

            if (entry.Entity is AdmissionPayment && OriginalStateEquals(entry, nameof(AdmissionPayment.State), "Successful"))
                throw new InvalidOperationException("A successful admission payment is immutable.");

            if (entry.Entity is SubscriptionPayment && OriginalStateEquals(entry, nameof(SubscriptionPayment.State), "Successful"))
                throw new InvalidOperationException("A successful subscription payment is immutable.");

            if (entry.Entity is PayrollPayment && OriginalStateEquals(entry, nameof(PayrollPayment.State), "Successful"))
                throw new InvalidOperationException("A successful payroll payment is immutable.");

            if (entry.Entity is StudentInvoice && OriginalStateEquals(entry, nameof(StudentInvoice.State), "Paid"))
                throw new InvalidOperationException("A paid student invoice is immutable. Use refund/credit/reversal workflow.");

            if (entry.Entity is Refund && OriginalStateEquals(entry, nameof(Refund.State), "Paid"))
                throw new InvalidOperationException("A paid refund is immutable.");

            if (entry.Entity is TransferCertificate)
            {
                if (!HasOnlyModifiedProperties(entry, nameof(TransferCertificate.FileAssetId))
                    || !OneTimeFileAttachmentIsValid(entry, nameof(TransferCertificate.FileAssetId)))
                {
                    throw new InvalidOperationException("A transfer certificate is immutable after issue; only the first generated file may be attached.");
                }
            }

            if (entry.Entity is CertificateIssue)
            {
                if (!HasOnlyModifiedProperties(
                        entry,
                        nameof(CertificateIssue.FileAssetId),
                        nameof(CertificateIssue.IsRevoked),
                        nameof(CertificateIssue.RevokedAt))
                    || !OneTimeFileAttachmentIfModifiedIsValid(entry, nameof(CertificateIssue.FileAssetId))
                    || !ControlledRevocationIsValid(entry, nameof(CertificateIssue.IsRevoked), nameof(CertificateIssue.RevokedAt)))
                {
                    throw new InvalidOperationException("An issued certificate is immutable except for one-time file attachment and controlled revocation.");
                }
            }

            if (entry.Entity is TranscriptIssue)
            {
                if (!HasOnlyModifiedProperties(
                        entry,
                        nameof(TranscriptIssue.FileAssetId),
                        nameof(TranscriptIssue.IsRevoked),
                        nameof(TranscriptIssue.RevokedAt))
                    || !OneTimeFileAttachmentIfModifiedIsValid(entry, nameof(TranscriptIssue.FileAssetId))
                    || !ControlledRevocationIsValid(entry, nameof(TranscriptIssue.IsRevoked), nameof(TranscriptIssue.RevokedAt)))
                {
                    throw new InvalidOperationException("An issued transcript is immutable except for one-time file attachment and controlled revocation.");
                }
            }
        }
    }

    private async Task ValidateAggregateImmutabilityAsync(
        IReadOnlyCollection<EntityEntry<BaseEntity>> entries,
        CancellationToken cancellationToken)
    {
        var changedJournalIds = entries
            .Where(x => x.State is EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .OfType<JournalLine>()
            .Select(x => x.JournalId)
            .Distinct()
            .ToArray();

        if (changedJournalIds.Length > 0)
        {
            var states = await Journals
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => changedJournalIds.Contains(x.Id))
                .Select(x => new { x.Id, x.State })
                .ToListAsync(cancellationToken);

            if (states.Any(x => string.Equals(x.State.ToString(), "Posted", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Lines of a posted journal are immutable. Create a reversal journal.");
        }

        var changedMovementIds = entries
            .Where(x => x.State is EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .OfType<InventoryMovementLine>()
            .Select(x => x.InventoryMovementId)
            .Distinct()
            .ToArray();

        if (changedMovementIds.Length > 0)
        {
            var postedExists = await InventoryMovements
                .IgnoreQueryFilters()
                .AsNoTracking()
                .AnyAsync(
                    x => changedMovementIds.Contains(x.Id) && x.StateCode == "Posted",
                    cancellationToken);

            if (postedExists)
                throw new InvalidOperationException("Lines of a posted inventory movement are immutable. Create a reversal movement.");
        }

        var changedPayrollEmployeeRunIds = entries
            .Where(x => x.State is EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .OfType<PayrollEmployee>()
            .Select(x => x.PayrollRunId)
            .Distinct()
            .ToList();

        var changedPayrollEmployeeIds = entries
            .Where(x => x.State is EntityState.Modified or EntityState.Deleted)
            .Select(x => x.Entity)
            .OfType<PayrollLine>()
            .Select(x => x.PayrollEmployeeId)
            .Distinct()
            .ToArray();

        if (changedPayrollEmployeeIds.Length > 0)
        {
            var runIds = await PayrollEmployees
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => changedPayrollEmployeeIds.Contains(x.Id))
                .Select(x => x.PayrollRunId)
                .Distinct()
                .ToListAsync(cancellationToken);

            changedPayrollEmployeeRunIds.AddRange(runIds);
        }

        if (changedPayrollEmployeeRunIds.Count > 0)
        {
            var runIds = changedPayrollEmployeeRunIds.Distinct().ToArray();
            var states = await PayrollRuns
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => runIds.Contains(x.Id))
                .Select(x => new { x.Id, x.State })
                .ToListAsync(cancellationToken);

            if (states.Any(x => string.Equals(x.State.ToString(), "Posted", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Details of a posted payroll run are immutable. Use a controlled correction/reversal.");
        }
    }

    private static bool OneTimeFileAttachmentIsValid(EntityEntry<BaseEntity> entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        return property.IsModified
            && property.OriginalValue == null
            && property.CurrentValue != null;
    }

    private static bool OneTimeFileAttachmentIfModifiedIsValid(EntityEntry<BaseEntity> entry, string propertyName)
    {
        var property = entry.Property(propertyName);
        return !property.IsModified
            || (property.OriginalValue == null && property.CurrentValue != null);
    }

    private static bool ControlledRevocationIsValid(
        EntityEntry<BaseEntity> entry,
        string isRevokedPropertyName,
        string revokedAtPropertyName)
    {
        var revoked = entry.Property(isRevokedPropertyName);
        var revokedAt = entry.Property(revokedAtPropertyName);

        if (!revoked.IsModified && !revokedAt.IsModified)
            return true;

        var originalRevoked = revoked.OriginalValue is bool oldValue && oldValue;
        var currentRevoked = revoked.CurrentValue is bool newValue && newValue;

        if (originalRevoked || !currentRevoked || revokedAt.CurrentValue == null)
            return false;

        return !revoked.IsModified || (revoked.OriginalValue is bool oldState && !oldState);
    }

    private static bool OriginalStateEquals(EntityEntry entry, string propertyName, string expected)
    {
        var value = entry.OriginalValues[propertyName];
        return string.Equals(value?.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsHardHistoryEntity(BaseEntity entity)
    {
        return entity is AuditLog
            or LearnerIdentityAccessLog
            or StudentPromotionRecord
            or StudentExitRecord
            or StudentStatusHistory
            or PersonMergeRecord
            or TransferCertificate
            or CertificateIssue
            or TranscriptIssue
            or AdmissionPayment
            or StudentInvoice
            or StudentInvoiceLine
            or StudentPayment
            or PaymentAllocation
            or Refund
            or RefundAllocation
            or Journal
            or JournalLine
            or PayrollRun
            or PayrollEmployee
            or PayrollLine
            or PayrollPayment
            or LoanAdvanceRecovery
            or InventoryMovement
            or InventoryMovementLine
            or StudentAttendanceAdjustment
            or EmployeeAttendanceAdjustment
            or StudentResultSummary;
    }

    private AuditLog? CreateAudit(EntityEntry entry, string action, DateTime now)
    {
        try
        {
            if (entry.Entity is AuditLog)
                return null;

            var log = new AuditLog
            {
                TenantId = entry.Entity switch
                {
                    ITenantScopedEntity tenantEntity => tenantEntity.TenantId,
                    ApplicationRole role => role.TenantId ?? TenantId,
                    _ => TenantId
                },
                UserId = UserId,
                UserName = UserName,
                Action = action,
                EntityName = entry.Entity.GetType().Name,
                EntityId = GetEntityId(entry),
                IpAddress = IpAddress,
                Endpoint = Endpoint,
                CorrelationId = CorrelationId,
                RequestId = RequestId,
                IsSuccess = true,
                OccurredAt = now,
                CreatedAt = now,
                CreatedBy = UserId
            };

            if (SuppressAuditPayload(entry.Entity))
                return log;

            switch (action)
            {
                case "Create":
                    log.NewValue = SerializeCurrentValues(entry);
                    break;

                case "Update":
                    var oldValues = new Dictionary<string, object?>();
                    var newValues = new Dictionary<string, object?>();

                    foreach (var property in entry.Properties.Where(x => x.IsModified && ShouldAuditProperty(x.Metadata.Name)))
                    {
                        oldValues[property.Metadata.Name] = property.OriginalValue;
                        newValues[property.Metadata.Name] = property.CurrentValue;
                    }

                    if (oldValues.Count > 0)
                    {
                        log.OldValue = JsonSerializer.Serialize(oldValues);
                        log.NewValue = JsonSerializer.Serialize(newValues);
                    }
                    break;

                case "Delete":
                    log.OldValue = JsonSerializer.Serialize(
                        entry.Properties
                            .Where(x => ShouldAuditProperty(x.Metadata.Name))
                            .ToDictionary(x => x.Metadata.Name, x => x.OriginalValue));
                    break;
            }

            return log;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Failed to create the audit record for {entry.Entity.GetType().Name}.",
                ex);
        }
    }

    private static string SerializeCurrentValues(EntityEntry entry)
    {
        return JsonSerializer.Serialize(
            entry.Properties
                .Where(x => ShouldAuditProperty(x.Metadata.Name))
                .ToDictionary(x => x.Metadata.Name, x => x.CurrentValue));
    }

    private static bool ShouldAuditProperty(string propertyName)
    {
        if (propertyName is nameof(BaseEntity.RowVersion)
            or nameof(BaseEntity.CreatedAt)
            or nameof(BaseEntity.CreatedBy)
            or nameof(BaseEntity.UpdatedAt)
            or nameof(BaseEntity.UpdatedBy)
            or nameof(BaseEntity.DeletedAt)
            or nameof(BaseEntity.DeletedBy))
        {
            return false;
        }

        return !IsSensitiveField(propertyName);
    }

    private static bool SuppressAuditPayload(object entity)
    {
        return entity is ApplicationUser
            or TenantSetting
            or AdmissionApplicant
            or AdmissionApplicantGuardian
            or AdmissionApplicantDocument
            or Student
            or Guardian
            or Person
            or PersonAddress
            or PersonIdentifier
            or StudentPersonLink
            or LearnerConsentRequest
            or LearnerDataGrant
            or LearnerIdentityAccessLog
            or Employee
            or EmployeeBankAccount
            or BankAccount
            or ApiCredential
            or TwoFactorAuth
            or TwoFactorRecoveryCode
            or RefreshToken;
    }

    private static bool IsSensitiveField(string name)
    {
        var sensitiveNames = new[]
        {
            "Password", "PasswordHash", "Secret", "Token", "ApiKey", "ApiSecret",
            "ProtectedCredential", "ProtectedSecret", "KeyHash", "TokenHash", "CodeHash",
            "CreditCard", "BankAccount", "AccountNumber", "NID", "NationalId", "BirthCert",
            "Passport", "RefreshToken", "SettingValue", "GatewayResponse", "Identifier",
            "LookupDigest", "ProtectedValue", "BackupCode"
        };

        return sensitiveNames.Any(x => name.Contains(x, StringComparison.OrdinalIgnoreCase));
    }

    private static long? GetEntityId(EntityEntry entry)
    {
        try
        {
            var property = entry.Property(nameof(BaseEntity.Id));
            return property.CurrentValue is long id ? id : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Narrow escape hatch for an authenticated learner/guardian resolving a consent request
    /// created in another tenant. The repository must prove ownership before invoking this method.
    /// </summary>
    internal async Task<int> SaveLearnerConsentDecisionAsync(
        long consentRequestId,
        long tenantId,
        long personId,
        long studentId,
        long userId,
        CancellationToken cancellationToken)
    {
        ResolveContext();

        if (consentRequestId <= 0
            || tenantId <= 0
            || personId <= 0
            || studentId <= 0
            || userId <= 0
            || UserId != userId)
        {
            throw new UnauthorizedAccessException("A valid authenticated learner-consent scope is required.");
        }

        if (_learnerConsentWriteScope != null)
            throw new InvalidOperationException("A learner-consent write is already in progress.");

        _learnerConsentWriteScope = new LearnerConsentWriteScope(
            consentRequestId,
            tenantId,
            personId,
            studentId,
            userId);

        try
        {
            return await SaveChangesAsync(cancellationToken);
        }
        finally
        {
            _learnerConsentWriteScope = null;
        }
    }

    private bool IsAuthorizedLearnerConsentWrite(
        EntityEntry<BaseEntity> entry,
        ITenantScopedEntity tenantEntity)
    {
        var scope = _learnerConsentWriteScope;
        if (scope == null
            || tenantEntity.TenantId != scope.TenantId
            || UserId != scope.UserId)
        {
            return false;
        }

        return entry.Entity switch
        {
            LearnerConsentRequest request =>
                entry.State == EntityState.Modified
                && request.Id == scope.ConsentRequestId
                && request.PersonId == scope.PersonId
                && request.RequestedStudentId == scope.StudentId
                && HasOnlyModifiedProperties(
                    entry,
                    nameof(LearnerConsentRequest.State),
                    nameof(LearnerConsentRequest.ResolvedAt),
                    nameof(LearnerConsentRequest.ResolvedByUserId)),

            LearnerDataGrant grant =>
                (entry.State is EntityState.Added or EntityState.Modified)
                && grant.LearnerConsentRequestId == scope.ConsentRequestId
                && grant.PersonId == scope.PersonId
                && grant.StudentId == scope.StudentId
                && (entry.State == EntityState.Added
                    || HasOnlyModifiedProperties(
                        entry,
                        nameof(LearnerDataGrant.State),
                        nameof(LearnerDataGrant.RevokedAt),
                        nameof(LearnerDataGrant.RevokedByUserId),
                        nameof(LearnerDataGrant.ExpiresAt))),

            StudentPersonLink link =>
                entry.State == EntityState.Added
                && link.PersonId == scope.PersonId
                && link.StudentId == scope.StudentId,

            LearnerIdentityAccessLog log =>
                entry.State == EntityState.Added
                && log.LearnerConsentRequestId == scope.ConsentRequestId
                && log.PersonId == scope.PersonId
                && log.StudentId == scope.StudentId
                && log.UserId == scope.UserId,

            _ => false
        };
    }

    private static bool HasOnlyModifiedProperties(
        EntityEntry<BaseEntity> entry,
        params string[] allowedProperties)
    {
        return entry.Properties
            .Where(x => x.IsModified)
            .All(x => allowedProperties.Contains(x.Metadata.Name));
    }

    #endregion

    #region IUnitOfWork Transactions

    public IExecutionStrategy CreateExecutionStrategy()
    {
        return Database.CreateExecutionStrategy();
    }

    public async Task BeginTransactionAsync()
    {
        if (_transaction != null || Database.CurrentTransaction != null)
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
