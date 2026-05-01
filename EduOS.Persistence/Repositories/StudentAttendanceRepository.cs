using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class StudentAttendanceRepository : GenericRepository<StudentAttendance>, IStudentAttendanceRepository
    {
        public StudentAttendanceRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<StudentAttendance>> GetByDateAsync(DateTime date, int classId, int sectionId)
        {
            return await _dbSet
                .Include(a => a.Student)
                .Where(a => a.Date.Date == date.Date 
                    && a.ClassId == classId 
                    && a.SectionId == sectionId)
                .OrderBy(a => a.Student!.Roll)
                .ToListAsync();
        }

        public async Task<List<StudentAttendance>> GetByStudentRangeAsync(int studentId, DateTime fromDate, DateTime toDate)
        {
            return await _dbSet
                .Where(a => a.StudentId == studentId 
                    && a.Date.Date >= fromDate.Date 
                    && a.Date.Date <= toDate.Date)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<StudentAttendance?> GetByStudentAndDateAsync(int studentId, DateTime date)
        {
            return await _dbSet
                .FirstOrDefaultAsync(a => a.StudentId == studentId && a.Date.Date == date.Date);
        }

        public async Task<bool> IsAlreadyMarkedAsync(int studentId, DateTime date)
        {
            return await _dbSet
                .AnyAsync(a => a.StudentId == studentId && a.Date.Date == date.Date);
        }

        public async Task<int> GetPresentCountAsync(int studentId, DateTime fromDate, DateTime toDate)
        {
            return await _dbSet.CountAsync(a => a.StudentId == studentId 
                && a.Date.Date >= fromDate.Date 
                && a.Date.Date <= toDate.Date 
                && a.Status == "Present");
        }

        public async Task<int> GetAbsentCountAsync(int studentId, DateTime fromDate, DateTime toDate)
        {
            return await _dbSet.CountAsync(a => a.StudentId == studentId 
                && a.Date.Date >= fromDate.Date 
                && a.Date.Date <= toDate.Date 
                && a.Status == "Absent");
        }

        public async Task<Dictionary<string, int>> GetMonthlyStatsAsync(int studentId, int month, int year)
        {
            var stats = await _dbSet
                .Where(a => a.StudentId == studentId 
                    && a.Date.Month == month 
                    && a.Date.Year == year)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            return stats.ToDictionary(s => s.Status, s => s.Count);
        }
    }
}
