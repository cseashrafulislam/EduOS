using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class ClassRoutine : TenantEntity
    {
        public int ClassId { get; set; }
        public int SectionId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public string DayName { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
    }
}
