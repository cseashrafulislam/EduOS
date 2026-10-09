using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class SubjectRepository : GenericRepository<Subject>, ISubjectRepository
{
    public SubjectRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<Subject>> GetByClassIdAsync(long classId)
    {
        var subjectIds = _context.Set<CurriculumSubject>()
            .Where(x => x.AcademicLevelId == classId && x.IsActive)
            .Select(x => x.SubjectId).Distinct();
        return await _dbSet.AsNoTracking().Where(x => subjectIds.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<List<Subject>> GetByClassAndGroupAsync(long classId, long? groupId)
    {
        var rows =
            from curriculumSubject in _context.Set<CurriculumSubject>()
            join curriculum in _context.Set<AcademicCurriculum>() on curriculumSubject.AcademicCurriculumId equals curriculum.Id
            where curriculumSubject.AcademicLevelId == classId
                && curriculumSubject.IsActive && curriculum.IsActive
                && (!groupId.HasValue || curriculum.AcademicTrackId == groupId || curriculum.AcademicTrackId == null)
            select curriculumSubject.SubjectId;
        var ids = rows.Distinct();
        return await _dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.Name).ToListAsync();
    }

    public async Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null)
    {
        var normalized = code.Trim();
        var query = _dbSet.Where(x => x.TenantId == tenantId && x.Code == normalized);
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }

    public Task<List<Subject>> GetByAcademicLevelAsync(long academicLevelId, CancellationToken cancellationToken)
    {
        var ids = _context.Set<CurriculumSubject>().Where(x => x.AcademicLevelId == academicLevelId && x.IsActive)
            .Select(x => x.SubjectId);
        return _dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).OrderBy(x => x.Name)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public Task<List<Subject>> GetByCurriculumAndLevelAsync(long academicCurriculumId, long academicLevelId, CancellationToken cancellationToken)
    {
        var ids = _context.Set<CurriculumSubject>().Where(x => x.AcademicCurriculumId == academicCurriculumId
            && x.AcademicLevelId == academicLevelId && x.IsActive).Select(x => x.SubjectId);
        return _dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive).OrderBy(x => x.Name)
            .ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId, CancellationToken cancellationToken)
    {
        var normalized = code.Trim();
        return _dbSet.AnyAsync(x => x.TenantId == tenantId && x.Code == normalized
            && (!excludeId.HasValue || x.Id != excludeId.Value), cancellationToken);
    }
}
