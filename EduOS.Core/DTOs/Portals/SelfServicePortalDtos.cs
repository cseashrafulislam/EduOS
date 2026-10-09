namespace EduOS.Core.DTOs.Portals;

public sealed class PortalStudentDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string RollNo { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long AcademicYearId { get; set; }
    public long AcademicLevelId { get; set; }
    public long AcademicBatchId { get; set; }
}

public sealed class PortalTimetableEntryDto
{
    public long RoutineId { get; set; }
    public DayOfWeek DayOfWeek { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string TeacherName { get; set; } = string.Empty;
    public string? RoomNo { get; set; }
}

public sealed class PortalAttendanceDto
{
    public long AttendanceSessionId { get; set; }
    public DateOnly AttendanceDate { get; set; }
    public AttendanceState State { get; set; }
    public TimeOnly? CheckInTime { get; set; }
    public TimeOnly? CheckOutTime { get; set; }
    public string? Remarks { get; set; }
}

public sealed class PortalResultDto
{
    public long ResultPublicationId { get; set; }
    public long AssessmentId { get; set; }
    public string AssessmentName { get; set; } = string.Empty;
    public int PublicationVersionNo { get; set; }
    public decimal ObtainedMarks { get; set; }
    public decimal TotalMarks { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? GPA { get; set; }
    public decimal? CGPA { get; set; }
    public string? GradeLetter { get; set; }
    public int? MeritPosition { get; set; }
    public bool IsPassed { get; set; }
    public bool IsWithheld { get; set; }
    public DateTime? PublishedAt { get; set; }
}

public sealed class PortalInvoiceDto
{
    public Guid Reference { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public InvoiceState State { get; set; }
}

public sealed class PortalPaymentDto
{
    public Guid Reference { get; set; }
    public string ReceiptNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public PaymentMethodType PaymentMethod { get; set; }
    public PaymentState State { get; set; }
    public DateOnly PaymentDate { get; set; }
}

public sealed class PortalFeeLedgerDto
{
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalDue { get; set; }
}

public sealed class PortalTransportDto
{
    public Guid Reference { get; set; }
    public string RouteName { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string? PickupPoint { get; set; }
    public string? DriverName { get; set; }
    public string? DriverPhone { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public decimal MonthlyFare { get; set; }
    public TransportAssignmentState State { get; set; }
}

public sealed class PortalHomeworkDto
{
    public Guid AssignmentReference { get; set; }
    public long SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public DateTime? OpensAt { get; set; }
    public DateTime? DueAt { get; set; }
}

public sealed class PortalAssignmentDto
{
    public Guid Reference { get; set; }
    public long CourseId { get; set; }
    public string CourseTitle { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Instructions { get; set; }
    public decimal MaxMarks { get; set; }
    public DateTime? DueAt { get; set; }
}
