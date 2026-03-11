using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class StudentGuardian : TenantEntity
    {
        public int StudentId { get; set; }
        public string GuardianName { get; set; }
        public string Relation { get; set; }
        public string Phone { get; set; }
        public string Address { get; set; }
    }
}
