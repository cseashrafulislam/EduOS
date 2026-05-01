using System;
using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.AI
{
    public class AttendancePrediction : BaseTenantEntity
    {
        public int StudentId { get; set; }
        public DateTime PredictionDate { get; set; }
        public decimal ProbabilityOfAbsence { get; set; }
        public string SuggestedAction { get; set; }
    }
}
