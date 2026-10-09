using EduOS.Core.Entities.Assessment;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class ExamRepository : GenericRepository<Assessment>, IAssessmentRepository
{
    public ExamRepository(EduOSDbContext context) : base(context) { }

    public Task<List<Assessment>> GetByYearAsync(long academicYearId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicYearId == academicYearId && x.State != AssessmentState.Cancelled)
            .OrderByDescending(x => x.StartDate).ToListAsync();

    public Task<List<Assessment>> GetPublishedAsync(long academicYearId) =>
        _dbSet.AsNoTracking().Where(x => x.AcademicYearId == academicYearId && x.State == AssessmentState.Published)
            .OrderByDescending(x => x.StartDate).ToListAsync();

    public Task<Assessment?> GetWithSchedulesAsync(long id) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);

    public Task<(List<Assessment> Items, int TotalCount)> GetByAcademicYearAsync(long academicYearId, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.AcademicYearId == academicYearId)
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id), page, pageSize, cancellationToken);
    public Task<(List<Assessment> Items, int TotalCount)> GetByStateAsync(long academicYearId, AssessmentState state, int page, int pageSize, CancellationToken cancellationToken) =>
        PageAsync(_dbSet.AsNoTracking().Where(x => x.AcademicYearId == academicYearId && x.State == state)
            .OrderByDescending(x => x.StartDate).ThenBy(x => x.Id), page, pageSize, cancellationToken);
}
