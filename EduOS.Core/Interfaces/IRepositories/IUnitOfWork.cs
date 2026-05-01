namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IUnitOfWork : IDisposable
    {
        // Auth
        IUserRepository Users { get; }
        IRoleRepository Roles { get; }
        IPermissionRepository Permissions { get; }

        // Tenant
        ITenantRepository Tenants { get; }

        // Academic
        IAcademicYearRepository AcademicYears { get; }
        IClassRepository Classes { get; }
        ISectionRepository Sections { get; }
        IGroupRepository Groups { get; }
        ISubjectRepository Subjects { get; }
        IDepartmentRepository Departments { get; }
        ISubjectTeacherRepository SubjectTeachers { get; }
        IClassRoutineRepository ClassRoutines { get; }

        // Students
        IStudentRepository Students { get; }
        IGuardianRepository Guardians { get; }
        IAdmissionRepository Admissions { get; }
        IEnrollmentRepository Enrollments { get; }

        // Employees
        IDesignationRepository Designations { get; }
        IEmployeeRepository Employees { get; }

        // Attendance
        IStudentAttendanceRepository StudentAttendances { get; }
        IEmployeeAttendanceRepository EmployeeAttendances { get; }
        ILeaveApplicationRepository LeaveApplications { get; }

        // Exams
        IExamRepository Exams { get; }
        IExamScheduleRepository ExamSchedules { get; }
        IMarkEntryRepository MarkEntries { get; }
        IResultRepository Results { get; }
        IGradeRuleRepository GradeRules { get; }

        // Finance
        IFeeHeadRepository FeeHeads { get; }
        IFeeStructureRepository FeeStructures { get; }
        IStudentInvoiceRepository StudentInvoices { get; }
        IPaymentRepository Payments { get; }

        // Communication
        INoticeRepository Notices { get; }
        INotificationRepository Notifications { get; }

        // Generic - for any entity not having specific repository
        IGenericRepository<T> Repository<T>() where T : class;

        // Save & Transaction
        Task<int> SaveChangesAsync();
        Task BeginTransactionAsync();
        Task CommitTransactionAsync();
        Task RollbackTransactionAsync();
    }
}
