namespace EduOS.Core.DTOs.Dashboard
{
    public class DashboardVm
    {
        public string InstitutionName { get; set; } = "N/A";
        public string InstitutionType { get; set; } = "N/A";
        public string OwnerName { get; set; } = "N/A";
        public string PlanName { get; set; } = "N/A";

        public int TotalStudents { get; set; }
        public int TotalTeachers { get; set; }
        public int TotalStaff { get; set; }
        public decimal MonthlyCollection { get; set; }

        public bool EmailVerified { get; set; }
        public bool SetupCompleted { get; set; }

        public int OnboardingPercent { get; set; }
        public int CurrentStep { get; set; }

        public DateTime? TrialEndDate { get; set; }
        public int ActiveFeatures { get; set; }
    }
}