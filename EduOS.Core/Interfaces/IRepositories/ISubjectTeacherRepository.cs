using EduOS.Core.Entities.Academic;

namespace EduOS.Core.Interfaces.IRepositories
{
    public interface ISubjectTeacherRepository : IGenericRepository<SubjectTeacher>
    {
        Task<List<SubjectTeacher>> GetByTeacherAsync(int teacherId, int academicYearId);
        Task<List<SubjectTeacher>> GetByClassSectionAsync(int classId, int sectionId);
        Task<SubjectTeacher?> GetClassTeacherAsync(int classId, int sectionId, int academicYearId);
    }
}
