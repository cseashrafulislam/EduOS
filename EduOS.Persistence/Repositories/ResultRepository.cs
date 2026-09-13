using EduOS.Core.Entities.Exams;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class ResultRepository : GenericRepository<ExamResult>, IResultRepository
    {
        public ResultRepository(EduOSDbContext context) : base(context) { }

        public async Task<ExamResult?> GetByExamAndStudentAsync(long examId, long studentId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(r => r.ExamId == examId && r.StudentId == studentId);
        }

        public async Task<List<ExamResult>> GetByExamAndClassAsync(long examId, long classId)
        {
            return await _dbSet
                .Include(r => r.Student)
                .Where(r => r.ExamId == examId && r.Student!.ClassId == classId)
                .OrderBy(r => r.Position)
                .ToListAsync();
        }

        public async Task<List<ExamResult>> GetTopRankersAsync(long examId, long classId, int top = 10)
        {
            return await _dbSet
                .Include(r => r.Student)
                .Where(r => r.ExamId == examId
                    && r.Student!.ClassId == classId
                    && r.IsPassed)
                .OrderBy(r => r.Position)
                .Take(top)
                .ToListAsync();
        }
    }
}
