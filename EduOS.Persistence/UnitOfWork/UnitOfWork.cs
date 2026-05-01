using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using EduOS.Persistence.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace EduOS.Persistence.UnitOfWork
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly EduOSDbContext _context;
        private IDbContextTransaction? _transaction;
        private readonly Dictionary<Type, object> _repositories = new();

        // Backing fields for specific repositories (lazy)
        private IUserRepository? _users;
        private IRoleRepository? _roles;
        private IPermissionRepository? _permissions;
        private ITenantRepository? _tenants;
        private IAcademicYearRepository? _academicYears;
        private IClassRepository? _classes;
        private ISectionRepository? _sections;
        private IGroupRepository? _groups;
        private ISubjectRepository? _subjects;
        private IDepartmentRepository? _departments;
        private ISubjectTeacherRepository? _subjectTeachers;
        private IClassRoutineRepository? _classRoutines;
        private IStudentRepository? _students;
        private IGuardianRepository? _guardians;
        private IAdmissionRepository? _admissions;
        private IEnrollmentRepository? _enrollments;
        private IDesignationRepository? _designations;
        private IEmployeeRepository? _employees;
        private IStudentAttendanceRepository? _studentAttendances;
        private IEmployeeAttendanceRepository? _employeeAttendances;
        private ILeaveApplicationRepository? _leaveApplications;
        private IExamRepository? _exams;
        private IExamScheduleRepository? _examSchedules;
        private IMarkEntryRepository? _markEntries;
        private IResultRepository? _results;
        private IGradeRuleRepository? _gradeRules;
        private IFeeHeadRepository? _feeHeads;
        private IFeeStructureRepository? _feeStructures;
        private IStudentInvoiceRepository? _studentInvoices;
        private IPaymentRepository? _payments;
        private INoticeRepository? _notices;
        private INotificationRepository? _notifications;

        public UnitOfWork(EduOSDbContext context)
        {
            _context = context;
        }

        // Auth
        public IUserRepository Users => _users ??= new UserRepository(_context);
        public IRoleRepository Roles => _roles ??= new RoleRepository(_context);
        public IPermissionRepository Permissions => _permissions ??= new PermissionRepository(_context);

        // Tenant
        public ITenantRepository Tenants => _tenants ??= new TenantRepository(_context);

        // Academic
        public IAcademicYearRepository AcademicYears => _academicYears ??= new AcademicYearRepository(_context);
        public IClassRepository Classes => _classes ??= new ClassRepository(_context);
        public ISectionRepository Sections => _sections ??= new SectionRepository(_context);
        public IGroupRepository Groups => _groups ??= new GroupRepository(_context);
        public ISubjectRepository Subjects => _subjects ??= new SubjectRepository(_context);
        public IDepartmentRepository Departments => _departments ??= new DepartmentRepository(_context);
        public ISubjectTeacherRepository SubjectTeachers => _subjectTeachers ??= new SubjectTeacherRepository(_context);
        public IClassRoutineRepository ClassRoutines => _classRoutines ??= new ClassRoutineRepository(_context);

        // Students
        public IStudentRepository Students => _students ??= new StudentRepository(_context);
        public IGuardianRepository Guardians => _guardians ??= new GuardianRepository(_context);
        public IAdmissionRepository Admissions => _admissions ??= new AdmissionRepository(_context);
        public IEnrollmentRepository Enrollments => _enrollments ??= new EnrollmentRepository(_context);

        // Employees
        public IDesignationRepository Designations => _designations ??= new DesignationRepository(_context);
        public IEmployeeRepository Employees => _employees ??= new EmployeeRepository(_context);

        // Attendance
        public IStudentAttendanceRepository StudentAttendances => _studentAttendances ??= new StudentAttendanceRepository(_context);
        public IEmployeeAttendanceRepository EmployeeAttendances => _employeeAttendances ??= new EmployeeAttendanceRepository(_context);
        public ILeaveApplicationRepository LeaveApplications => _leaveApplications ??= new LeaveApplicationRepository(_context);

        // Exams
        public IExamRepository Exams => _exams ??= new ExamRepository(_context);
        public IExamScheduleRepository ExamSchedules => _examSchedules ??= new ExamScheduleRepository(_context);
        public IMarkEntryRepository MarkEntries => _markEntries ??= new MarkEntryRepository(_context);
        public IResultRepository Results => _results ??= new ResultRepository(_context);
        public IGradeRuleRepository GradeRules => _gradeRules ??= new GradeRuleRepository(_context);

        // Finance
        public IFeeHeadRepository FeeHeads => _feeHeads ??= new FeeHeadRepository(_context);
        public IFeeStructureRepository FeeStructures => _feeStructures ??= new FeeStructureRepository(_context);
        public IStudentInvoiceRepository StudentInvoices => _studentInvoices ??= new StudentInvoiceRepository(_context);
        public IPaymentRepository Payments => _payments ??= new PaymentRepository(_context);

        // Communication
        public INoticeRepository Notices => _notices ??= new NoticeRepository(_context);
        public INotificationRepository Notifications => _notifications ??= new NotificationRepository(_context);

        // Generic Repository (for entities without specific repository)
        public IGenericRepository<T> Repository<T>() where T : class
        {
            if (_repositories.TryGetValue(typeof(T), out var repo))
                return (IGenericRepository<T>)repo;

            var newRepo = new GenericRepository<T>(_context);
            _repositories[typeof(T)] = newRepo;
            return newRepo;
        }

        // Save & Transactions
        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.CommitAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            _transaction?.Dispose();
            _context.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
