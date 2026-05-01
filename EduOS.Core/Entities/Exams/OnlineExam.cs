using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class OnlineExam : BaseTenantEntity
    {
        public string Title { get; set; } = string.Empty;
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Duration { get; set; } // minutes
        public int TotalMark { get; set; }
        public int PassMark { get; set; }
        public string Status { get; set; } = "Draft"; // Draft/Published/Completed

        public virtual Class? Class { get; set; }
        public virtual Subject? Subject { get; set; }
    }
}
