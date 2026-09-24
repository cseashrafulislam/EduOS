using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentRepository : IGenericRepository<Student>
    {
        Task<Student?> GetByCodeAsync(string code);
        Task<Student?> GetByUserIdAsync(long userId);
        Task<Student?> GetWithGuardiansAsync(long id);
        Task<List<Student>> GetByClassSectionAsync(long classId, long sectionId);
        Task<List<Student>> GetByAcademicYearAsync(long academicYearId);
        Task<bool> IsCodeExistsAsync(string code, long tenantId, long? excludeId = null);
        Task<bool> IsRollExistsInSectionAsync(string roll, long classId, long sectionId, long academicYearId, long? excludeId = null);
        Task<string> GenerateStudentCodeAsync(long tenantId, long academicYearId);
        Task<int> GetActiveCountAsync(long tenantId);
    }
}
