using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Academic
{
    public class SubjectPrerequisite : BaseTenantEntity
    {
        public long SubjectId { get; set; }
        public long PrerequisiteSubjectId { get; set; }

        public bool IsMandatory { get; set; } = true;

        public virtual Subject? Subject { get; set; }
        public virtual Subject? PrerequisiteSubject { get; set; }
    }
}