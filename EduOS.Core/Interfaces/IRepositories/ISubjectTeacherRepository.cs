using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface IInstructorAssignmentRepository : IGenericRepository<InstructorAssignment>
    {
        Task<List<InstructorAssignment>> GetByTeacherAsync(int teacherId, int academicYearId);
        Task<List<InstructorAssignment>> GetByClassSectionAsync(int classId, int sectionId);
        Task<InstructorAssignment?> GetClassTeacherAsync(int classId, int sectionId, int academicYearId);
    }
}
