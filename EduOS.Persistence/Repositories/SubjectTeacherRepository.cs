using EduOS.Core.Entities.Academic;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class InstructorAssignmentRepository : GenericRepository<InstructorAssignment>, IInstructorAssignmentRepository
    {
        public InstructorAssignmentRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<InstructorAssignment>> GetByTeacherAsync(int teacherId, int academicYearId)
        {
            return await _dbSet
                .Include(st => st.Class)
                .Include(st => st.Section)
                .Include(st => st.Subject)
                .Where(st => st.TeacherId == teacherId && st.AcademicYearId == academicYearId)
                .ToListAsync();
        }

        public async Task<List<InstructorAssignment>> GetByClassSectionAsync(int classId, int sectionId)
        {
            return await _dbSet
                .Include(st => st.Subject)
                .Include(st => st.Teacher)
                .Where(st => st.ClassId == classId && st.SectionId == sectionId)
                .ToListAsync();
        }

        public async Task<InstructorAssignment?> GetClassTeacherAsync(int classId, int sectionId, int academicYearId)
        {
            return await _dbSet
                .Include(st => st.Teacher)
                .FirstOrDefaultAsync(st => st.ClassId == classId 
                    && st.SectionId == sectionId 
                    && st.AcademicYearId == academicYearId 
                    && st.IsClassTeacher);
        }
    }
}
