using EduOS.Core.Entities.Exams;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class MarkEntryRepository : GenericRepository<MarkEntry>, IMarkEntryRepository
    {
        public MarkEntryRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<MarkEntry>> GetByExamAndStudentAsync(long examId, long studentId)
        {
            return await _dbSet
                .Include(m => m.Subject)
                .Where(m => m.ExamId == examId && m.StudentId == studentId)
                .ToListAsync();
        }

        public async Task<List<MarkEntry>> GetByExamAndSubjectAsync(long examId, long subjectId, long classId)
        {
            return await _dbSet
                .Include(m => m.Student)
                .Where(m => m.ExamId == examId && m.SubjectId == subjectId
                    && m.Student!.ClassId == classId)
                .OrderBy(m => m.Student!.Roll)
                .ToListAsync();
        }

        public async Task<MarkEntry?> GetExistingAsync(long examId, long studentId, long subjectId)
        {
            return await _dbSet.FirstOrDefaultAsync(m =>
                m.ExamId == examId && m.StudentId == studentId && m.SubjectId == subjectId);
        }

        public async Task<bool> IsAllMarksEnteredAsync(long examId, long classId)
        {
            var totalStudents = await _context.Students
                .CountAsync(s => s.ClassId == classId && s.IsActive);

            var totalSubjects = await _context.Subjects
                .CountAsync(s => s.ClassId == classId && s.IsActive);

            var expectedEntries = totalStudents * totalSubjects;

            var actualEntries = await _dbSet
                .CountAsync(m => m.ExamId == examId
                    && m.Student!.ClassId == classId);

            return actualEntries >= expectedEntries;
        }
    }
}
