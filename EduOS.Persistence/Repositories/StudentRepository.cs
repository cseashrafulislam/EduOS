using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Students;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class StudentRepository : GenericRepository<Student>, IStudentRepository
{
    public StudentRepository(EduOSDbContext context) : base(context) { }

    public Task<Student?> GetByCodeAsync(string code) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.StudentCode == code);

    public Task<Student?> GetByUserIdAsync(long userId) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);

    public Task<Student?> GetWithGuardiansAsync(long id) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    public async Task<List<Student>> GetByClassSectionAsync(long classId, long sectionId)
    {
        var studentIds = _context.Set<StudentEnrollment>()
            .Where(x => x.AcademicLevelId == classId && x.AcademicBatchId == sectionId
                && x.IsCurrent && x.State == EnrollmentState.Active)
            .Select(x => x.StudentId);
        return await _dbSet.AsNoTracking().Where(x => studentIds.Contains(x.Id) && x.StatusCode == "Active")
            .OrderBy(x => x.StudentCode).ToListAsync();
    }

    public async Task<List<Student>> GetByAcademicYearAsync(long academicYearId)
    {
        var studentIds = _context.Set<StudentEnrollment>()
            .Where(x => x.AcademicYearId == academicYearId && x.State == EnrollmentState.Active)
            .Select(x => x.StudentId).Distinct();
        return await _dbSet.AsNoTracking().Where(x => studentIds.Contains(x.Id) && x.StatusCode == "Active")
            .OrderBy(x => x.StudentCode).ToListAsync();
    }

    public async Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null)
    {
        var normalized = code.Trim();
        var query = _dbSet.Where(x => x.TenantId == tenantId && x.StudentCode == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public async Task<bool> IsRollExistsInSectionAsync(
        string roll, long classId, long sectionId, long academicYearId, long? excludeId = null)
    {
        var normalized = roll.Trim();
        var query = _context.Set<StudentEnrollment>().Where(x =>
            x.RollNo == normalized
            && x.AcademicLevelId == classId
            && x.AcademicBatchId == sectionId
            && x.AcademicYearId == academicYearId
            && x.State == EnrollmentState.Active);
        if (excludeId.HasValue) query = query.Where(x => x.StudentId != excludeId.Value);
        return await query.AnyAsync();
    }

    public async Task<string> GenerateStudentCodeAsync(long tenantId, long academicYearId)
    {
        var year = await _context.AcademicYears.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.Id == academicYearId)
            .Select(x => x.Code)
            .FirstOrDefaultAsync();
        var prefix = string.IsNullOrWhiteSpace(year) ? DateTime.UtcNow.Year.ToString() : year.Trim();
        var suffix = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
        return $"STD-{prefix}-{suffix}";
    }

    public Task<int> GetActiveCountAsync(long tenantId) =>
        _dbSet.CountAsync(x => x.TenantId == tenantId && x.StatusCode == "Active");

    public Task<Student?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.StudentCode == code, cancellationToken);
    public Task<Student?> GetByUserIdAsync(long userId, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
    public Task<(List<Student> Items, int TotalCount)> GetByAcademicBatchAsync(long academicBatchId, bool currentOnly, int page, int pageSize, CancellationToken cancellationToken)
    {
        var ids = _context.Set<StudentEnrollment>().Where(x => x.AcademicBatchId == academicBatchId && x.State == EnrollmentState.Active
            && (!currentOnly || x.IsCurrent)).Select(x => x.StudentId).Distinct();
        return PageAsync(_dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.StatusCode == "Active")
            .OrderBy(x => x.StudentCode).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    }
    public Task<(List<Student> Items, int TotalCount)> GetByAcademicYearAsync(long academicYearId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var ids = _context.Set<StudentEnrollment>().Where(x => x.AcademicYearId == academicYearId && x.State == EnrollmentState.Active)
            .Select(x => x.StudentId).Distinct();
        return PageAsync(_dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.StatusCode == "Active")
            .OrderBy(x => x.StudentCode).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    }
    public Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim();
        return _dbSet.AnyAsync(x => x.TenantId == tenantId && x.StudentCode == normalized
            && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
    }
    public Task<bool> IsRollAssignedInBatchAsync(string rollNo, long academicBatchId, long? excludeEnrollmentId, CancellationToken cancellationToken)
    {
        var normalized = rollNo.Trim();
        return _context.Set<StudentEnrollment>().AnyAsync(x => x.AcademicBatchId == academicBatchId && x.RollNo == normalized
            && x.State == EnrollmentState.Active && (!excludeEnrollmentId.HasValue || x.Id != excludeEnrollmentId.Value), cancellationToken);
    }
    public Task<int> GetActiveCountAsync(long tenantId, CancellationToken cancellationToken) =>
        _dbSet.CountAsync(x => x.TenantId == tenantId && x.StatusCode == "Active", cancellationToken);
}
