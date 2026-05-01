using EduOS.Core.Entities.Exams;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class ExamScheduleRepository : GenericRepository<ExamSchedule>, IExamScheduleRepository
    {
        public ExamScheduleRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<ExamSchedule>> GetByExamAndClassAsync(int examId, int classId)
        {
            return await _dbSet
                .Include(s => s.Subject)
                .Where(s => s.ExamId == examId && s.ClassId == classId)
                .OrderBy(s => s.ExamDate)
                .ToListAsync();
        }

        public async Task<List<ExamSchedule>> GetByDateAsync(DateTime date, int tenantId)
        {
            return await _dbSet
                .Include(s => s.Subject)
                .Include(s => s.Class)
                .Where(s => s.ExamDate.Date == date.Date && s.TenantId == tenantId)
                .ToListAsync();
        }
    }
}
