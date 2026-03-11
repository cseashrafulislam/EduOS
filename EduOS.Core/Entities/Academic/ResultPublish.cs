using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.Academic
{
    public class ResultPublish : TenantEntity
    {
        public int ExamId { get; set; }
        public DateTime PublishDate { get; set; }
        public bool IsPublished { get; set; }
    }
}
