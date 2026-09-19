using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class GuardianRepository : GenericRepository<Guardian>, IGuardianRepository
    {
        public GuardianRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Guardian>> GetByStudentIdAsync(long studentId)
        {
            return await _dbSet
                .Where(g => g.StudentId == studentId)
                .OrderByDescending(g => g.IsPrimary)
                .ToListAsync();
        }

        public async Task<Guardian?> GetPrimaryByStudentIdAsync(long studentId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(g => g.StudentId == studentId && g.IsPrimary);
        }

        public async Task<Guardian?> GetByPhoneAsync(string phone)
        {
            return await _dbSet
                .FirstOrDefaultAsync(g => g.Phone == phone);
        }
    }
}
