using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class ProgramCampus : BaseTenantEntity
    {
        public long AcademicProgramId { get; set; }
        public long CampusId { get; set; }

        public bool IsAdmissionOpen { get; set; } = true;
        public bool IsActive { get; set; } = true;

        public virtual AcademicProgram? AcademicProgram { get; set; }
    }
}