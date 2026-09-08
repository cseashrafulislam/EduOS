using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.Persistence.Extensions
{
    public static class PersistenceServiceExtensions
    {
        public static IServiceCollection AddPersistenceServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            // ==================== DbContext ====================
            services.AddDbContext<EduOSDbContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sql =>
                    {
                        sql.MigrationsAssembly(typeof(EduOSDbContext).Assembly.FullName);
                        sql.EnableRetryOnFailure(maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                        sql.CommandTimeout(60);
                    }));

            // ==================== IUnitOfWork ====================
            // EduOSDbContext implements IUnitOfWork.
            // Register it so services that inject IUnitOfWork get the same
            // scoped DbContext instance (not a new one).
            services.AddScoped<IUnitOfWork>(sp =>
                sp.GetRequiredService<EduOSDbContext>());

            // ==================== Generic Repository ====================
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            // ==================== Specific Repositories ====================
            RegisterAuthRepositories(services);
            RegisterTenantRepositories(services);
            RegisterAcademicRepositories(services);
            RegisterStudentRepositories(services);
            RegisterEmployeeRepositories(services);
            RegisterAttendanceRepositories(services);
            RegisterExamRepositories(services);
            RegisterFinanceRepositories(services);
            RegisterCommunicationRepositories(services);
            RegisterSubscriptionRepositories(services);

            return services;
        }

        private static void RegisterAuthRepositories(IServiceCollection s)
        {
            s.AddScoped<IPermissionRepository, PermissionRepository>();
        }

        private static void RegisterTenantRepositories(IServiceCollection s)
        {
            s.AddScoped<ITenantRepository, TenantRepository>();
        }

        private static void RegisterAcademicRepositories(IServiceCollection s)
        {
            s.AddScoped<IAcademicYearRepository, AcademicYearRepository>();
            s.AddScoped<IClassRepository, ClassRepository>();
            s.AddScoped<ISectionRepository, SectionRepository>();
            s.AddScoped<IGroupRepository, GroupRepository>();
            s.AddScoped<ISubjectRepository, SubjectRepository>();
            s.AddScoped<IDepartmentRepository, DepartmentRepository>();
            s.AddScoped<IInstructorAssignmentRepository, InstructorAssignmentRepository>();
            s.AddScoped<IRoutineEntryRepository, RoutineEntryRepository>();
        }

        private static void RegisterStudentRepositories(IServiceCollection s)
        {
            s.AddScoped<IStudentRepository, StudentRepository>();
            s.AddScoped<IGuardianRepository, GuardianRepository>();
            s.AddScoped<IAdmissionRepository, AdmissionRepository>();
            s.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        }

        private static void RegisterEmployeeRepositories(IServiceCollection s)
        {
            s.AddScoped<IDesignationRepository, DesignationRepository>();
            s.AddScoped<IEmployeeRepository, EmployeeRepository>();
        }

        private static void RegisterAttendanceRepositories(IServiceCollection s)
        {
            s.AddScoped<IStudentAttendanceRepository, StudentAttendanceRepository>();
            s.AddScoped<IEmployeeAttendanceRepository, EmployeeAttendanceRepository>();
            s.AddScoped<ILeaveApplicationRepository, LeaveApplicationRepository>();
        }

        private static void RegisterExamRepositories(IServiceCollection s)
        {
            s.AddScoped<IExamRepository, ExamRepository>();
            s.AddScoped<IExamScheduleRepository, ExamScheduleRepository>();
            s.AddScoped<IMarkEntryRepository, MarkEntryRepository>();
            s.AddScoped<IResultRepository, ResultRepository>();
            s.AddScoped<IGradeRuleRepository, GradeRuleRepository>();
        }

        private static void RegisterFinanceRepositories(IServiceCollection s)
        {
            s.AddScoped<IFeeHeadRepository, FeeHeadRepository>();
            s.AddScoped<IFeeStructureRepository, FeeStructureRepository>();
            s.AddScoped<IStudentInvoiceRepository, StudentInvoiceRepository>();
            s.AddScoped<IPaymentRepository, PaymentRepository>();
        }

        private static void RegisterCommunicationRepositories(IServiceCollection s)
        {
            s.AddScoped<INoticeRepository, NoticeRepository>();
            s.AddScoped<INotificationRepository, NotificationRepository>();
        }

        private static void RegisterSubscriptionRepositories(IServiceCollection s)
        {
            s.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
            s.AddScoped<ITenantSubscriptionRepository, TenantSubscriptionRepository>();
            s.AddScoped<ISubscriptionInvoiceRepository, SubscriptionInvoiceRepository>();
            s.AddScoped<ISubscriptionPaymentRepository, SubscriptionPaymentRepository>();
        }
    }
}
