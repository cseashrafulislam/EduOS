using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories;

public interface IEnrollmentRepository : IGenericRepository<StudentEnrollment>
{
    Task<List<StudentEnrollment>> GetByStudentIdAsync(long studentId);
    Task<StudentEnrollment?> GetCurrentAsync(long studentId, long academicYearId);
    Task<List<StudentEnrollment>> GetByClassSectionAsync(long classId, long sectionId, long academicYearId);
}
