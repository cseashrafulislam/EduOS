using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>RoutineEntry belongs to SubjectOffering + RoutineTimeSlot; effective-dated slot/instructor conflicts must be checked transactionally.</summary>
public interface IRoutineEntryRepository : IGenericRepository<RoutineEntry>
{
    Task<List<RoutineEntry>> GetByAcademicBatchAsync(long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
    Task<List<RoutineEntry>> GetByInstructorAsync(long employeeId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
    Task<List<RoutineEntry>> GetByDayAsync(DayOfWeek dayOfWeek, long academicBatchId, DateOnly effectiveOn, CancellationToken cancellationToken = default);
    Task<bool> HasInstructorConflictAsync(long employeeId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, DateOnly effectiveFrom, DateOnly? effectiveTo, long? excludeRoutineEntryId = null, CancellationToken cancellationToken = default);
    Task<bool> HasRoomConflictAsync(long roomId, DayOfWeek dayOfWeek, TimeOnly startTime, TimeOnly endTime, DateOnly effectiveFrom, DateOnly? effectiveTo, long? excludeRoutineEntryId = null, CancellationToken cancellationToken = default);
}
