using EduOS.Core.Entities.Attendance;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class StudentAttendanceRepository : GenericRepository<StudentAttendance>, IStudentAttendanceRepository
    {
        public StudentAttendanceRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<StudentAttendance>> GetByDateAsync(DateTime date, long classId, long sectionId)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .Include(a => a.Student)
                .Where(a => a.Date >= dayStart && a.Date < dayEnd && a.ClassId == classId && a.SectionId == sectionId)
                .OrderBy(a => a.Student!.Roll)
                .ToListAsync();
        }

        public async Task<List<StudentAttendance>> GetByStudentRangeAsync(long studentId, DateTime fromDate, DateTime toDate)
        {
            var rangeStart = fromDate.Date;
            var rangeEndExclusive = toDate.Date.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .Where(a => a.StudentId == studentId && a.Date >= rangeStart && a.Date < rangeEndExclusive)
                .OrderBy(a => a.Date)
                .ToListAsync();
        }

        public async Task<StudentAttendance?> GetByStudentAndDateAsync(long studentId, DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.StudentId == studentId && a.Date >= dayStart && a.Date < dayEnd);
        }

        public async Task<bool> IsAlreadyMarkedAsync(long studentId, DateTime date)
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            return await _dbSet.AnyAsync(a => a.StudentId == studentId && a.Date >= dayStart && a.Date < dayEnd);
        }

        public async Task<int> GetPresentCountAsync(long studentId, DateTime fromDate, DateTime toDate)
        {
            var rangeStart = fromDate.Date;
            var rangeEndExclusive = toDate.Date.AddDays(1);

            return await _dbSet.CountAsync(a =>
                a.StudentId == studentId &&
                a.Date >= rangeStart &&
                a.Date < rangeEndExclusive &&
                a.Status == "Present");
        }

        public async Task<int> GetAbsentCountAsync(long studentId, DateTime fromDate, DateTime toDate)
        {
            var rangeStart = fromDate.Date;
            var rangeEndExclusive = toDate.Date.AddDays(1);

            return await _dbSet.CountAsync(a =>
                a.StudentId == studentId &&
                a.Date >= rangeStart &&
                a.Date < rangeEndExclusive &&
                a.Status == "Absent");
        }

        public async Task<Dictionary<string, int>> GetMonthlyStatsAsync(long studentId, int month, int year)
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEndExclusive = monthStart.AddMonths(1);

            var stats = await _dbSet
                .AsNoTracking()
                .Where(a => a.StudentId == studentId && a.Date >= monthStart && a.Date < monthEndExclusive)
                .GroupBy(a => a.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            return stats.ToDictionary(s => s.Status, s => s.Count);
        }
    }
}
