using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IRoutineEntryRepository : IGenericRepository<RoutineEntry>
    {
        Task<List<RoutineEntry>> GetByClassSectionAsync(int classId, int sectionId, int academicYearId);
        Task<List<RoutineEntry>> GetByTeacherAsync(int teacherId, int academicYearId);
        Task<List<RoutineEntry>> GetByDayAsync(string dayOfWeek, int classId, int sectionId);
        Task<bool> HasConflictAsync(int teacherId, string dayOfWeek, TimeSpan startTime, TimeSpan endTime, int? excludeId = null);
    }
}
