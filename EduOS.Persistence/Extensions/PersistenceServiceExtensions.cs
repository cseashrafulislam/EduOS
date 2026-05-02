using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using EduOS.Persistence.Repositories.SaaS;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduOS.Persistence.Extensions
{
    /// <summary>
    /// Registers all persistence layer services: DbContext, repositories, etc.
    /// </summary>
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
                    sqlOptions =>
                    {
                        sqlOptions.MigrationsAssembly(typeof(EduOSDbContext).Assembly.FullName);
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                        sqlOptions.CommandTimeout(60);
                    }));

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

        private static void RegisterAuthRepositories(IServiceCollection services)
        {
            services.AddScoped<IPermissionRepository, PermissionRepository>();
        }

        private static void RegisterTenantRepositories(IServiceCollection services)
        {
            services.AddScoped<ITenantRepository, TenantRepository>();
        }

        private static void RegisterAcademicRepositories(IServiceCollection services)
        {
            services.AddScoped<IAcademicYearRepository, AcademicYearRepository>();
            services.AddScoped<IClassRepository, ClassRepository>();
            services.AddScoped<ISectionRepository, SectionRepository>();
            services.AddScoped<IGroupRepository, GroupRepository>();
            services.AddScoped<ISubjectRepository, SubjectRepository>();
            services.AddScoped<IDepartmentRepository, DepartmentRepository>();
            services.AddScoped<ISubjectTeacherRepository, SubjectTeacherRepository>();
            services.AddScoped<IClassRoutineRepository, ClassRoutineRepository>();
        }

        private static void RegisterStudentRepositories(IServiceCollection services)
        {
            services.AddScoped<IStudentRepository, StudentRepository>();
            services.AddScoped<IGuardianRepository, GuardianRepository>();
            services.AddScoped<IAdmissionRepository, AdmissionRepository>();
            services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();
        }

        private static void RegisterEmployeeRepositories(IServiceCollection services)
        {
            services.AddScoped<IDesignationRepository, DesignationRepository>();
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();
        }

        private static void RegisterAttendanceRepositories(IServiceCollection services)
        {
            services.AddScoped<IStudentAttendanceRepository, StudentAttendanceRepository>();
            services.AddScoped<IEmployeeAttendanceRepository, EmployeeAttendanceRepository>();
            services.AddScoped<ILeaveApplicationRepository, LeaveApplicationRepository>();
        }

        private static void RegisterExamRepositories(IServiceCollection services)
        {
            services.AddScoped<IExamRepository, ExamRepository>();
            services.AddScoped<IExamScheduleRepository, ExamScheduleRepository>();
            services.AddScoped<IMarkEntryRepository, MarkEntryRepository>();
            services.AddScoped<IResultRepository, ResultRepository>();
            services.AddScoped<IGradeRuleRepository, GradeRuleRepository>();
        }

        private static void RegisterFinanceRepositories(IServiceCollection services)
        {
            services.AddScoped<IFeeHeadRepository, FeeHeadRepository>();
            services.AddScoped<IFeeStructureRepository, FeeStructureRepository>();
            services.AddScoped<IStudentInvoiceRepository, StudentInvoiceRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
        }

        private static void RegisterCommunicationRepositories(IServiceCollection services)
        {
            services.AddScoped<INoticeRepository, NoticeRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
        }

        private static void RegisterSubscriptionRepositories(IServiceCollection services)
        {
            // Phase B - SaaS subscription repositories
            services.AddScoped<ISubscriptionPlanRepository, SubscriptionPlanRepository>();
            services.AddScoped<ITenantSubscriptionRepository, TenantSubscriptionRepository>();
            services.AddScoped<ISubscriptionInvoiceRepository, SubscriptionInvoiceRepository>();
            services.AddScoped<ISubscriptionPaymentRepository, SubscriptionPaymentRepository>();
        }
    }
}
