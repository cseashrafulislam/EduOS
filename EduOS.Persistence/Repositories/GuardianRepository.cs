using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class GuardianRepository : GenericRepository<Guardian>, IGuardianRepository
{
    public GuardianRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<Guardian>> GetByStudentIdAsync(long studentId)
    {
        var ids = _context.Set<StudentGuardian>().Where(x => x.StudentId == studentId).Select(x => x.GuardianId);
        return await _dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.FullName).ToListAsync();
    }

    public async Task<Guardian?> GetPrimaryByStudentIdAsync(long studentId)
    {
        var guardianId = await _context.Set<StudentGuardian>().AsNoTracking()
            .Where(x => x.StudentId == studentId && x.IsPrimary)
            .Select(x => (long?)x.GuardianId).FirstOrDefaultAsync();
        return guardianId.HasValue
            ? await _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Id == guardianId.Value)
            : null;
    }

    public Task<Guardian?> GetByPhoneAsync(string phone) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Phone == phone);

    public async Task<List<Guardian>> GetByStudentIdAsync(long studentId, CancellationToken cancellationToken)
    {
        var ids = _context.Set<StudentGuardian>().Where(x => x.StudentId == studentId).Select(x => x.GuardianId);
        return await _dbSet.AsNoTracking().Where(x => ids.Contains(x.Id) && x.IsActive)
            .OrderBy(x => x.FullName).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public async Task<Guardian?> GetPrimaryByStudentIdAsync(long studentId, CancellationToken cancellationToken)
    {
        var ids = _context.Set<StudentGuardian>().Where(x => x.StudentId == studentId && x.IsPrimary).Select(x => x.GuardianId);
        return await _dbSet.AsNoTracking().FirstOrDefaultAsync(x => ids.Contains(x.Id) && x.IsActive, cancellationToken);
    }
    public Task<Guardian?> GetByPhoneAsync(string phone, CancellationToken cancellationToken) =>
        _dbSet.AsNoTracking().FirstOrDefaultAsync(x => x.Phone == phone, cancellationToken);
}
