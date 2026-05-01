using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
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
            // DbContext registration
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
                    }));

            // Generic Repository
            services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));

            // Specific Repositories
            RegisterRepositories(services);
            return services;
        }

        private static void RegisterRepositories(IServiceCollection services)
        {
            // Auth
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IRoleRepository, RoleRepository>();
            services.AddScoped<IPermissionRepository, PermissionRepository>();

            // Tenant
            services.AddScoped<ITenantRepository, TenantRepository>();

            // Academic
            services.AddScoped<IAcademicYearRepository, AcademicYearRepository>();
            services.AddScoped<IClassRepository, ClassRepository>();
            services.AddScoped<ISectionRepository, SectionRepository>();
            services.AddScoped<IGroupRepository, GroupRepository>();
            services.AddScoped<ISubjectRepository, SubjectRepository>();
            services.AddScoped<IDepartmentRepository, DepartmentRepository>();
            services.AddScoped<ISubjectTeacherRepository, SubjectTeacherRepository>();
            services.AddScoped<IClassRoutineRepository, ClassRoutineRepository>();

            // Students
            services.AddScoped<IStudentRepository, StudentRepository>();
            services.AddScoped<IGuardianRepository, GuardianRepository>();
            services.AddScoped<IAdmissionRepository, AdmissionRepository>();
            services.AddScoped<IEnrollmentRepository, EnrollmentRepository>();

            // Employees
            services.AddScoped<IDesignationRepository, DesignationRepository>();
            services.AddScoped<IEmployeeRepository, EmployeeRepository>();

            // Attendance
            services.AddScoped<IStudentAttendanceRepository, StudentAttendanceRepository>();
            services.AddScoped<IEmployeeAttendanceRepository, EmployeeAttendanceRepository>();
            services.AddScoped<ILeaveApplicationRepository, LeaveApplicationRepository>();

            // Exams
            services.AddScoped<IExamRepository, ExamRepository>();
            services.AddScoped<IExamScheduleRepository, ExamScheduleRepository>();
            services.AddScoped<IMarkEntryRepository, MarkEntryRepository>();
            services.AddScoped<IResultRepository, ResultRepository>();
            services.AddScoped<IGradeRuleRepository, GradeRuleRepository>();

            // Finance
            services.AddScoped<IFeeHeadRepository, FeeHeadRepository>();
            services.AddScoped<IFeeStructureRepository, FeeStructureRepository>();
            services.AddScoped<IStudentInvoiceRepository, StudentInvoiceRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();

            // Communication
            services.AddScoped<INoticeRepository, NoticeRepository>();
            services.AddScoped<INotificationRepository, NotificationRepository>();
        }
    }
}
