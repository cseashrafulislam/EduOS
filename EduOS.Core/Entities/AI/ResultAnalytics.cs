using System;
using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.AI
{
    public class ResultAnalytics : TenantEntity
    {
        public int StudentId { get; set; }
        public int ExamId { get; set; }
        public decimal AverageMarks { get; set; }
        public string StrengthAreas { get; set; }
        public string WeakAreas { get; set; }
        public string Recommendation { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
    }
}
