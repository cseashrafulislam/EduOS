using EduOS.Core.Entities.Students;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Repositories
{
    public class StudentRepository : GenericRepository<Student>, IStudentRepository
    {
        public StudentRepository(EduOSDbContext context) : base(context) { }

        public async Task<Student?> GetByCodeAsync(string code)
        {
            return await _dbSet
                .Include(s => s.Class)
                .Include(s => s.Section)
                .Include(s => s.Group)
                .FirstOrDefaultAsync(s => s.StudentCode == code);
        }

        public async Task<Student?> GetByUserIdAsync(int userId)
        {
            return await _dbSet
                .FirstOrDefaultAsync(s => s.UserId == userId);
        }

        public async Task<Student?> GetWithGuardiansAsync(int id)
        {
            return await _dbSet
                .Include(s => s.Guardians)
                .Include(s => s.Class)
                .Include(s => s.Section)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task<List<Student>> GetByClassSectionAsync(int classId, int sectionId)
        {
            return await _dbSet
                .Where(s => s.ClassId == classId 
                    && s.SectionId == sectionId 
                    && s.Status == "Active")
                .OrderBy(s => s.Roll)
                .ToListAsync();
        }

        public async Task<List<Student>> GetByAcademicYearAsync(int academicYearId)
        {
            return await _dbSet
                .Where(s => s.AcademicYearId == academicYearId && s.IsActive)
                .ToListAsync();
        }

        public async Task<bool> IsCodeExistsAsync(string code, int tenantId, int? excludeId = null)
        {
            var query = _dbSet.Where(s => 
                s.StudentCode == code && s.TenantId == tenantId);
            if (excludeId.HasValue)
                query = query.Where(s => s.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<bool> IsRollExistsInSectionAsync(
            string roll, int classId, int sectionId, int academicYearId, int? excludeId = null)
        {
            var query = _dbSet.Where(s => s.Roll == roll 
                && s.ClassId == classId 
                && s.SectionId == sectionId 
                && s.AcademicYearId == academicYearId);
            if (excludeId.HasValue)
                query = query.Where(s => s.Id != excludeId.Value);
            return await query.AnyAsync();
        }

        public async Task<string> GenerateStudentCodeAsync(int tenantId, int academicYearId)
        {
            var year = await _context.AcademicYears
                .FirstOrDefaultAsync(y => y.Id == academicYearId);
            var yearStr = year?.Name ?? DateTime.UtcNow.Year.ToString();

            var lastStudent = await _dbSet
                .Where(s => s.TenantId == tenantId && s.AcademicYearId == academicYearId)
                .OrderByDescending(s => s.Id)
                .FirstOrDefaultAsync();

            int nextNumber = 1;
            if (lastStudent != null && !string.IsNullOrEmpty(lastStudent.StudentCode))
            {
                var numericPart = new string(lastStudent.StudentCode
                    .SkipWhile(c => !char.IsDigit(c))
                    .Where(char.IsDigit).ToArray());
                if (numericPart.Length > 4 && int.TryParse(numericPart.Substring(4), out int lastNumber))
                    nextNumber = lastNumber + 1;
            }

            return $"STD{yearStr}{nextNumber:D4}";
        }

        public async Task<int> GetActiveCountAsync(int tenantId)
        {
            return await _dbSet
                .CountAsync(s => s.TenantId == tenantId && s.IsActive && s.Status == "Active");
        }
    }
}
