using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IClassRoutineRepository : IGenericRepository<ClassRoutine>
    {
        Task<List<ClassRoutine>> GetByClassSectionAsync(int classId, int sectionId, int academicYearId);
        Task<List<ClassRoutine>> GetByTeacherAsync(int teacherId, int academicYearId);
        Task<List<ClassRoutine>> GetByDayAsync(string dayOfWeek, int classId, int sectionId);
        Task<bool> HasConflictAsync(int teacherId, string dayOfWeek, TimeSpan startTime, TimeSpan endTime, int? excludeId = null);
    }
}
