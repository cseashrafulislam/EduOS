using EduOS.Core.Entities.Students;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IStudentRepository : IGenericRepository<Student>
    {
        Task<Student?> GetByCodeAsync(string code);
        Task<Student?> GetByUserIdAsync(int userId);
        Task<Student?> GetWithGuardiansAsync(int id);
        Task<List<Student>> GetByClassSectionAsync(int classId, int sectionId);
        Task<List<Student>> GetByAcademicYearAsync(int academicYearId);
        Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null);
        Task<bool> IsRollExistsInSectionAsync(string roll, int classId, int sectionId, int academicYearId, int? excludeId = null);
        Task<string> GenerateStudentCodeAsync(int tenantId, int academicYearId);
        Task<int> GetActiveCountAsync(int tenantId);
    }
}
