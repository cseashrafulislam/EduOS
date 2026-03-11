using System;
using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.AI
{
    public class AttendancePrediction : TenantEntity
    {
        public int StudentId { get; set; }
        public DateTime PredictionDate { get; set; }
        public decimal ProbabilityOfAbsence { get; set; }
        public string SuggestedAction { get; set; }
    }
}
