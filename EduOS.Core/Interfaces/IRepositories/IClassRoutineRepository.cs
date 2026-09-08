using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IRoutineEntryRepository : IGenericRepository<RoutineEntry>
    {
        Task<List<RoutineEntry>> GetByBatchAsync(long academicBatchId, long academicYearId);
        Task<List<RoutineEntry>> GetByEmployeeAsync(long employeeId, long academicYearId);
        Task<List<RoutineEntry>> GetByDayAsync(DayOfWeek dayOfWeek, long academicBatchId);
        Task<bool> HasConflictAsync(long employeeId, DayOfWeek dayOfWeek, TimeSpan startTime, TimeSpan endTime, long? excludeId = null);
    }
}
