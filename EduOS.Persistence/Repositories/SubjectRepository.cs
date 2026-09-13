using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class SubjectRepository : GenericRepository<Subject>, ISubjectRepository
    {
        public SubjectRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Subject>> GetByClassIdAsync(long classId)
        {
            return await _dbSet
                .Where(s => s.ClassId == classId && s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<List<Subject>> GetByClassAndGroupAsync(long classId, long? groupId)
        {
            return await _dbSet
                .Where(s => s.ClassId == classId
                    && (s.GroupId == groupId || s.GroupId == null)
                    && s.IsActive)
                .OrderBy(s => s.Name)
                .ToListAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null)
        {
            var query = _dbSet.Where(s =>
                s.Code.ToLower() == code.ToLower() && s.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(s => s.Id != excludeId.Value);
            return await query.AnyAsync();
        }
    }
}
