using EduOS.Core.Entities.Attendance;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentAttendanceRepository : IGenericRepository<StudentAttendance>
    {
        Task<List<StudentAttendance>> GetByDateAsync(DateTime date, int classId, int sectionId);
        Task<List<StudentAttendance>> GetByStudentRangeAsync(int studentId, DateTime fromDate, DateTime toDate);
        Task<StudentAttendance?> GetByStudentAndDateAsync(int studentId, DateTime date);
        Task<bool> IsAlreadyMarkedAsync(int studentId, DateTime date);
        Task<int> GetPresentCountAsync(int studentId, DateTime fromDate, DateTime toDate);
        Task<int> GetAbsentCountAsync(int studentId, DateTime fromDate, DateTime toDate);
        Task<Dictionary<string, int>> GetMonthlyStatsAsync(int studentId, int month, int year);
    }
}
