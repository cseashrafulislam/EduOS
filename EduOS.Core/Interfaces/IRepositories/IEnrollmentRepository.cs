using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEnrollmentRepository : IGenericRepository<Enrollment>
    {
        Task<List<Enrollment>> GetByStudentIdAsync(long studentId);
        Task<Enrollment?> GetCurrentAsync(long studentId, long academicYearId);
        Task<List<Enrollment>> GetByClassSectionAsync(long classId, long sectionId, long academicYearId);
    }
}
