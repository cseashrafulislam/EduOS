namespace EduOS.Core.Enums.Academics
{
    public enum AcademicCycleType
    {
        Annual = 1,
        Semester = 2,
        Trimester = 3,
        Quarterly = 4,
        Modular = 5,
        BatchBased = 6
    }

    public enum DeliveryMode
    {
        OnCampus = 1,
        Online = 2,
        Hybrid = 3
    }

    public enum SubjectType
    {
        Core = 1,
        Elective = 2,
        Practical = 3,
        Lab = 4,
        Viva = 5,
        NonCredit = 6
    }

    public enum AttendanceStatus
    {
        Present = 1,
        Absent = 2,
        Late = 3,
        Leave = 4,
        Excused = 5
    }

    public enum EmployeeAttendanceStatus
    {
        Present = 1,
        Absent = 2,
        Late = 3,
        Leave = 4,
        HalfDay = 5
    }

    public enum AssessmentType
    {
        Exam = 1,
        Quiz = 2,
        Assignment = 3,
        Practical = 4,
        Viva = 5,
        Project = 6,
        Presentation = 7
    }

    public enum GuardianRelationType
    {
        Father = 1,
        Mother = 2,
        Spouse = 3,
        Brother = 4,
        Sister = 5,
        Uncle = 6,
        Aunt = 7,
        Other = 8
    }

    public enum GenderType
    {
        Male = 1,
        Female = 2,
        Other = 3
    }

    public enum EnrollmentStatus
    {
        Active = 1,
        Promoted = 2,
        Completed = 3,
        Dropped = 4,
        Suspended = 5,
        Archived = 6
    }

    public enum CalendarEventType
    {
        Holiday = 1,
        Exam = 2,
        Class = 3,
        Seminar = 4,
        Workshop = 5,
        Notice = 6,
        Other = 7
    }
}