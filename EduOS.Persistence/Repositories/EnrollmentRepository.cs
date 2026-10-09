using EduOS.Core.Entities.Academic;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class EnrollmentRepository : GenericRepository<StudentEnrollment>, IStudentEnrollmentRepository
{
    public EnrollmentRepository(EduOSDbContext context) : base(context) { }

    public Task<List<StudentEnrollment>> GetByStudentIdAsync(long studentId) =>
        _dbSet.AsNoTracking().Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrollmentDate).ToListAsync();

    public Task<StudentEnrollment?> GetCurrentAsync(long studentId, long academicYearId) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.StudentId == studentId
            && x.AcademicYearId == academicYearId && x.IsCurrent && x.State == EnrollmentState.Active);

    public Task<List<StudentEnrollment>> GetByClassSectionAsync(long classId, long sectionId, long academicYearId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicLevelId == classId && x.AcademicBatchId == sectionId
            && x.AcademicYearId == academicYearId && x.State == EnrollmentState.Active)
            .OrderBy(x => x.RollNo).ToListAsync();

    public Task<List<StudentEnrollment>> GetByStudentIdAsync(long studentId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().Where(x => x.StudentId == studentId)
            .OrderByDescending(x => x.EnrollmentDate).ThenByDescending(x => x.Id).ToListAsync(cancellationToken);
    public Task<StudentEnrollment?> GetCurrentAsync(long studentId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.StudentId == studentId && x.IsCurrent
            && x.State == EnrollmentState.Active && x.State == EnrollmentState.Active, cancellationToken);
    public Task<(List<StudentEnrollment> Items, int TotalCount)> GetByAcademicBatchAsync(long academicBatchId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.AcademicBatchId == academicBatchId)
            .OrderBy(x => x.RollNo).ThenBy(x => x.Id), page, pageSize, cancellationToken);
}
