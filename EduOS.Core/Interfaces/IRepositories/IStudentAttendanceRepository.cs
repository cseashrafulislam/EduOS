using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentAttendanceRepository : IGenericRepository<StudentAttendance>
    {
        Task<List<StudentAttendance>> GetByDateAsync(DateTime date, long classId, long sectionId);
        Task<List<StudentAttendance>> GetByStudentRangeAsync(long studentId, DateTime fromDate, DateTime toDate);
        Task<StudentAttendance?> GetByStudentAndDateAsync(long studentId, DateTime date);
        Task<bool> IsAlreadyMarkedAsync(long studentId, DateTime date);
        Task<int> GetPresentCountAsync(long studentId, DateTime fromDate, DateTime toDate);
        Task<int> GetAbsentCountAsync(long studentId, DateTime fromDate, DateTime toDate);
        Task<Dictionary<string, int>> GetMonthlyStatsAsync(long studentId, int month, int year);
    }
}
