using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class EnrollmentRepository : GenericRepository<Enrollment>, IEnrollmentRepository
    {
        public EnrollmentRepository(EduOSDbContext context) : base(context) { }

        public async Task<List<Enrollment>> GetByStudentIdAsync(long studentId) => await _dbSet.Include(e => e.Class).Include(e => e.Section).Include(e => e.AcademicYear).Where(e => e.StudentId == studentId).OrderByDescending(e => e.EnrollmentDate).ToListAsync();

        public async Task<Enrollment?> GetCurrentAsync(long studentId, long academicYearId) => await _dbSet.FirstOrDefaultAsync(e => e.StudentId == studentId && e.AcademicYearId == academicYearId && e.IsActive);

        public async Task<List<Enrollment>> GetByClassSectionAsync(long classId, long sectionId, long academicYearId) => await _dbSet.Include(e => e.Student).Where(e => e.ClassId == classId && e.SectionId == sectionId && e.AcademicYearId == academicYearId && e.IsActive).OrderBy(e => e.Roll).ToListAsync();
    }
}
