using EduOS.Core.Entities.Attendance;
using EduOS.Core.Enums.Domain;

namespace EduOS.Core.Interfaces.IRepositories;

/// <summary>Attendance identity is (AttendanceSessionId, StudentEnrollmentId); a student may have multiple sessions on one date.</summary>
public interface IStudentAttendanceRepository : IGenericRepository<StudentAttendance>
{
    Task<List<StudentAttendance>> GetBySessionAsync(long attendanceSessionId, CancellationToken cancellationToken = default);
    Task<List<StudentAttendance>> GetByEnrollmentRangeAsync(long studentEnrollmentId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
    Task<StudentAttendance?> GetBySessionAndEnrollmentAsync(long attendanceSessionId, long studentEnrollmentId, CancellationToken cancellationToken = default);
    Task<bool> IsAlreadyMarkedAsync(long attendanceSessionId, long studentEnrollmentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<AttendanceState, int>> GetStateCountsAsync(long studentEnrollmentId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken = default);
}
