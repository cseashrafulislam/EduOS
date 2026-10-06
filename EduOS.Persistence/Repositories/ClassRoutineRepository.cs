using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class RoutineEntryRepository : GenericRepository<RoutineEntry>, IRoutineEntryRepository
{
    public RoutineEntryRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<RoutineEntry>> GetByBatchAsync(long academicBatchId, long academicYearId)
    {
        var query =
            from entry in _dbSet.AsNoTracking()
            join offering in _context.Set<SubjectOffering>() on entry.SubjectOfferingId equals offering.Id
            join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
            where offering.AcademicBatchId == academicBatchId && offering.AcademicYearId == academicYearId && entry.IsActive
            orderby entry.DayOfWeek, slot.StartTime
            select entry;
        return await query.ToListAsync();
    }

    public async Task<List<RoutineEntry>> GetByEmployeeAsync(long employeeId, long academicYearId)
    {
        var query =
            from entry in _dbSet.AsNoTracking()
            join assignment in _context.Set<InstructorAssignment>() on entry.InstructorAssignmentId equals assignment.Id
            join offering in _context.Set<SubjectOffering>() on entry.SubjectOfferingId equals offering.Id
            join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
            where assignment.EmployeeId == employeeId && offering.AcademicYearId == academicYearId && entry.IsActive
            orderby entry.DayOfWeek, slot.StartTime
            select entry;
        return await query.ToListAsync();
    }

    public async Task<List<RoutineEntry>> GetByDayAsync(DayOfWeek dayOfWeek, long academicBatchId)
    {
        var query =
            from entry in _dbSet.AsNoTracking()
            join offering in _context.Set<SubjectOffering>() on entry.SubjectOfferingId equals offering.Id
            join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
            where entry.DayOfWeek == dayOfWeek && offering.AcademicBatchId == academicBatchId && entry.IsActive
            orderby slot.StartTime
            select entry;
        return await query.ToListAsync();
    }

    public async Task<bool> HasConflictAsync(
        long employeeId, DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan endTime, long? excludeId = null)
    {
        if (endTime <= startTime) return true;
        var start = TimeOnly.FromTimeSpan(startTime);
        var end = TimeOnly.FromTimeSpan(endTime);
        var query =
            from entry in _dbSet
            join assignment in _context.Set<InstructorAssignment>() on entry.InstructorAssignmentId equals assignment.Id
            join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
            where assignment.EmployeeId == employeeId && entry.DayOfWeek == dayOfWeek && entry.IsActive
                && slot.StartTime < end && slot.EndTime > start
            select entry;
        if (excludeId.HasValue) query = query.Where(x => x.Id != excludeId.Value);
        return await query.AnyAsync();
    }
}
