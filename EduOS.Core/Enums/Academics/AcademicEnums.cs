namespace EduOS.Core.Enums.Academics
{
    public enum DeliveryMode
    {
        OnCampus = 1,
        OnlineLive = 2,
        OnlineSelfPaced = 3,
        Hybrid = 4
    }

    public enum SubjectType
    {
        Core = 1,
        Elective = 2,
        Optional = 3,
        Practical = 4,
        Lab = 5
    }

    public enum EnrollmentStatus
    {
        Active = 1,
        Completed = 2,
        Promoted = 3,
        Transferred = 4,
        Withdrawn = 5,
        Suspended = 6,
        Dropped = 7
    }

    public enum AttendanceStatus
    {
        Present = 1,
        Absent = 2,
        Late = 3,
        Leave = 4,
        Excused = 5
    }

    public enum AssessmentType
    {
        Exam = 1,
        Quiz = 2,
        Assignment = 3,
        ClassTest = 4,
        Practical = 5,
        Viva = 6,
        Project = 7
    }

    public enum CalendarEventType
    {
        Academic = 1,
        Holiday = 2,
        Examination = 3,
        Admission = 4,
        Sports = 5,
        Cultural = 6,
        Meeting = 7,
        Other = 8
    }

    public enum LearningContentType
    {
        Text = 1,
        Video = 2,
        Audio = 3,
        Pdf = 4,
        Document = 5,
        ExternalLink = 6,
        LiveClass = 7,
        Assignment = 8,
        Quiz = 9
    }

    public enum CourseAccessType
    {
        Free = 1,
        Paid = 2,
        Subscription = 3,
        InstitutionOnly = 4
    }

    public enum CourseEnrollmentStatus
    {
        Active = 1,
        Completed = 2,
        Expired = 3,
        Cancelled = 4
    }
}