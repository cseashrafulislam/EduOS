using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class RoutineEntryRepository : GenericRepository<RoutineEntry>, IRoutineEntryRepository
    {
        public RoutineEntryRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<RoutineEntry>> GetByClassSectionAsync(int classId, int sectionId, int academicYearId)
        {
            return await _dbSet
                .Include(r => r.Subject)
                .Include(r => r.Teacher)
                .Where(r => r.ClassId == classId 
                    && r.SectionId == sectionId 
                    && r.AcademicYearId == academicYearId)
                .OrderBy(r => r.DayOfWeek)
                .ThenBy(r => r.StartTime)
                .ToListAsync();
        }

        public async Task<List<RoutineEntry>> GetByTeacherAsync(int teacherId, int academicYearId)
        {
            return await _dbSet
                .Include(r => r.Class)
                .Include(r => r.Section)
                .Include(r => r.Subject)
                .Where(r => r.TeacherId == teacherId && r.AcademicYearId == academicYearId)
                .OrderBy(r => r.DayOfWeek)
                .ThenBy(r => r.StartTime)
                .ToListAsync();
        }

        public async Task<List<RoutineEntry>> GetByDayAsync(string dayOfWeek, int classId, int sectionId)
        {
            return await _dbSet
                .Include(r => r.Subject)
                .Include(r => r.Teacher)
                .Where(r => r.DayOfWeek == dayOfWeek 
                    && r.ClassId == classId 
                    && r.SectionId == sectionId)
                .OrderBy(r => r.StartTime)
                .ToListAsync();
        }

        public async Task<bool> HasConflictAsync(
            int teacherId, string dayOfWeek, TimeSpan startTime, TimeSpan endTime, int? excludeId = null)
        {
            var query = _dbSet.Where(r => 
                r.TeacherId == teacherId 
                && r.DayOfWeek == dayOfWeek
                && ((r.StartTime <= startTime && r.EndTime > startTime)
                    || (r.StartTime < endTime && r.EndTime >= endTime)
                    || (r.StartTime >= startTime && r.EndTime <= endTime)));

            if (excludeId.HasValue)
                query = query.Where(r => r.Id != excludeId.Value);

            return await query.AnyAsync();
        }
    }
}
