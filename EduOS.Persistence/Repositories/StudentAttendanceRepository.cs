using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Attendance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories;

public class StudentAttendanceRepository : GenericRepository<StudentAttendance>, IStudentAttendanceRepository
{
    public StudentAttendanceRepository(EduOSDbContext context) : base(context) { }

    public async Task<List<StudentAttendance>> GetByDateAsync(DateTime date, long classId, long sectionId)
    {
        var target = DateOnly.FromDateTime(date);
        var query =
            from attendance in _dbSet.AsNoTracking()
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where session.AttendanceDate == target
                && enrollment.AcademicLevelId == classId
                && enrollment.AcademicBatchId == sectionId
            orderby enrollment.RollNo
            select attendance;
        return await query.ToListAsync();
    }

    public async Task<List<StudentAttendance>> GetByStudentRangeAsync(long studentId, DateTime fromDate, DateTime toDate)
    {
        var from = DateOnly.FromDateTime(fromDate);
        var to = DateOnly.FromDateTime(toDate);
        var query =
            from attendance in _dbSet.AsNoTracking()
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where enrollment.StudentId == studentId && session.AttendanceDate >= from && session.AttendanceDate <= to
            orderby session.AttendanceDate
            select attendance;
        return await query.ToListAsync();
    }

    public async Task<StudentAttendance?> GetByStudentAndDateAsync(long studentId, DateTime date)
    {
        var target = DateOnly.FromDateTime(date);
        var query =
            from attendance in _dbSet.AsNoTracking()
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where enrollment.StudentId == studentId && session.AttendanceDate == target
            orderby session.StartsAt
            select attendance;
        return await query.FirstOrDefaultAsync();
    }

    public async Task<bool> IsAlreadyMarkedAsync(long studentId, DateTime date)
    {
        var target = DateOnly.FromDateTime(date);
        var query =
            from attendance in _dbSet
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where enrollment.StudentId == studentId && session.AttendanceDate == target
            select attendance.Id;
        return await query.AnyAsync();
    }

    public Task<int> GetPresentCountAsync(long studentId, DateTime fromDate, DateTime toDate) =>
        CountStateAsync(studentId, fromDate, toDate, AttendanceState.Present);

    public Task<int> GetAbsentCountAsync(long studentId, DateTime fromDate, DateTime toDate) =>
        CountStateAsync(studentId, fromDate, toDate, AttendanceState.Absent);

    public async Task<Dictionary<string, int>> GetMonthlyStatsAsync(long studentId, int month, int year)
    {
        var start = new DateOnly(year, month, 1);
        var end = start.AddMonths(1);
        var query =
            from attendance in _dbSet.AsNoTracking()
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where enrollment.StudentId == studentId && session.AttendanceDate >= start && session.AttendanceDate < end
            group attendance by attendance.State into g
            select new { State = g.Key, Count = g.Count() };
        var rows = await query.ToListAsync();
        return rows.ToDictionary(x => x.State.ToString(), x => x.Count, StringComparer.OrdinalIgnoreCase);
    }

    private async Task<int> CountStateAsync(long studentId, DateTime fromDate, DateTime toDate, AttendanceState state)
    {
        var from = DateOnly.FromDateTime(fromDate);
        var to = DateOnly.FromDateTime(toDate);
        var query =
            from attendance in _dbSet
            join session in _context.Set<AttendanceSession>() on attendance.AttendanceSessionId equals session.Id
            join enrollment in _context.Set<StudentEnrollment>() on attendance.StudentEnrollmentId equals enrollment.Id
            where enrollment.StudentId == studentId && session.AttendanceDate >= from && session.AttendanceDate <= to
                && attendance.State == state
            select attendance.Id;
        return await query.CountAsync();
    }
}
