using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Exams
{
    public class ExamHall : BaseTenantEntity
    {
        public string HallName { get; set; } = string.Empty;
        public string? RoomNo { get; set; }
        public int Capacity { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
