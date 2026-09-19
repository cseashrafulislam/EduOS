using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class RoutineEntryRepository : GenericRepository<RoutineEntry>, IRoutineEntryRepository
    {
        public RoutineEntryRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<RoutineEntry>> GetByBatchAsync(long academicBatchId, long academicYearId)
        {
            return await _dbSet
                .Include(r => r.AcademicBatch)
                .Include(r => r.RoutineTimeSlot)
                .Include(r => r.Subject)
                .Include(r => r.Employee)
                .Where(r => r.AcademicBatchId == academicBatchId && r.AcademicYearId == academicYearId)
                .OrderBy(r => r.DayOfWeek)
                .ThenBy(r => r.RoutineTimeSlot!.StartTime)
                .ToListAsync();
        }

        public async Task<List<RoutineEntry>> GetByEmployeeAsync(long employeeId, long academicYearId)
        {
            return await _dbSet
                .Include(r => r.AcademicBatch)
                .Include(r => r.RoutineTimeSlot)
                .Include(r => r.Subject)
                .Include(r => r.Employee)
                .Where(r => r.EmployeeId == employeeId && r.AcademicYearId == academicYearId)
                .OrderBy(r => r.DayOfWeek)
                .ThenBy(r => r.RoutineTimeSlot!.StartTime)
                .ToListAsync();
        }

        public async Task<List<RoutineEntry>> GetByDayAsync(DayOfWeek dayOfWeek, long academicBatchId)
        {
            return await _dbSet
                .Include(r => r.AcademicBatch)
                .Include(r => r.RoutineTimeSlot)
                .Include(r => r.Subject)
                .Include(r => r.Employee)
                .Where(r => r.DayOfWeek == dayOfWeek && r.AcademicBatchId == academicBatchId)
                .OrderBy(r => r.RoutineTimeSlot!.StartTime)
                .ToListAsync();
        }

        public async Task<bool> HasConflictAsync(long employeeId, DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan endTime, long? excludeId = null)
        {
            var query = _dbSet.Where(r => r.EmployeeId == employeeId && r.DayOfWeek == dayOfWeek && r.RoutineTimeSlot != null
                && r.RoutineTimeSlot.StartTime < endTime && r.RoutineTimeSlot.EndTime > startTime);

            if (excludeId.HasValue) query = query.Where(r => r.Id != excludeId.Value);

            return await query.AnyAsync();
        }
    }
}
