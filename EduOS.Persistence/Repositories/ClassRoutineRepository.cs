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

    public async Task<List<RoutineEntry>> GetByAcademicBatchAsync(long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        var offerings = _context.Set<SubjectOffering>().Where(x => x.AcademicBatchId == academicBatchId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => offerings.Contains(x.SubjectOfferingId) && x.IsActive
            && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public async Task<List<RoutineEntry>> GetByInstructorAsync(long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        var assignmentIds = _context.Set<InstructorAssignment>().Where(x => x.EmployeeId == employeeId && x.IsActive
            && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn)).Select(x => (long?)x.Id);
        return await _dbSet.AsNoTracking().Where(x => assignmentIds.Contains(x.InstructorAssignmentId)
            && x.IsActive && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderBy(x => x.DayOfWeek).ThenBy(x => x.RoutineTimeSlotId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public async Task<List<RoutineEntry>> GetByDayAsync(DayOfWeek dayOfWeek, long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken)
    {
        var offerings = _context.Set<SubjectOffering>().Where(x => x.AcademicBatchId == academicBatchId).Select(x => x.Id);
        return await _dbSet.AsNoTracking().Where(x => x.DayOfWeek == dayOfWeek && offerings.Contains(x.SubjectOfferingId)
            && x.IsActive && x.EffectiveFrom <= effectiveOn && (x.EffectiveTo == null || x.EffectiveTo >= effectiveOn))
            .OrderBy(x => x.RoutineTimeSlotId).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }
    public Task<bool> HasInstructorConflictAsync(long employeeId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long? excludeRoutineEntryId, CancellationToken cancellationToken)
    {
        if (startTime >= endTime || (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)) return Task.FromResult(true);
        var query = from entry in _dbSet
                    join assignment in _context.Set<InstructorAssignment>() on entry.InstructorAssignmentId equals assignment.Id
                    join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
                    where assignment.EmployeeId == employeeId && assignment.IsActive && entry.IsActive
                        && entry.DayOfWeek == dayOfWeek && slot.StartTime < endTime && slot.EndTime > startTime
                        && (!effectiveTo.HasValue || entry.EffectiveFrom <= effectiveTo.Value)
                        && (entry.EffectiveTo == null || entry.EffectiveTo >= effectiveFrom)
                        && (!excludeRoutineEntryId.HasValue || entry.Id != excludeRoutineEntryId.Value)
                    select entry.Id;
        return query.AnyAsync(cancellationToken);
    }
    public Task<bool> HasRoomConflictAsync(long roomId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime,
        DateOnly effectiveFrom, DateOnly? effectiveTo, long? excludeRoutineEntryId, CancellationToken cancellationToken)
    {
        if (startTime >= endTime || (effectiveTo.HasValue && effectiveTo.Value < effectiveFrom)) return Task.FromResult(true);
        var query = from entry in _dbSet
                    join slot in _context.Set<RoutineTimeSlot>() on entry.RoutineTimeSlotId equals slot.Id
                    where entry.RoomId == roomId && entry.IsActive && entry.DayOfWeek == dayOfWeek
                        && slot.StartTime < endTime && slot.EndTime > startTime
                        && (!effectiveTo.HasValue || entry.EffectiveFrom <= effectiveTo.Value)
                        && (entry.EffectiveTo == null || entry.EffectiveTo >= effectiveFrom)
                        && (!excludeRoutineEntryId.HasValue || entry.Id != excludeRoutineEntryId.Value)
                    select entry.Id;
        return query.AnyAsync(cancellationToken);
    }
}
