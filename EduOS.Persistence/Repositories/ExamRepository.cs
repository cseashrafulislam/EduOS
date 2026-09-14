using EduOS.Core.Entities.Exams;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class ExamRepository : GenericRepository<Exam>, IExamRepository
    {
        public ExamRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Exam>> GetByYearAsync(long academicYearId)
        {
            return await _dbSet
                .Where(e => e.AcademicYearId == academicYearId && e.IsActive)
                .OrderByDescending(e => e.StartDate)
                .ToListAsync();
        }

        public async Task<List<Exam>> GetPublishedAsync(long academicYearId)
        {
            return await _dbSet
                .Where(e => e.AcademicYearId == academicYearId && e.IsPublished)
                .ToListAsync();
        }

        public async Task<Exam?> GetWithSchedulesAsync(long id)
        {
            return await _context.ExamSchedules
                .Where(s => s.ExamId == id)
                .Include(s => s.Subject)
                .Include(s => s.Class)
                .Select(s => s.Exam)
                .FirstOrDefaultAsync();
        }
    }
}
