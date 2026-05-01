using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IEnrollmentRepository : IGenericRepository<Enrollment>
    {
        Task<List<Enrollment>> GetByStudentIdAsync(int studentId);
        Task<Enrollment?> GetCurrentAsync(int studentId, int academicYearId);
        Task<List<Enrollment>> GetByClassSectionAsync(int classId, int sectionId, int academicYearId);
    }
}
