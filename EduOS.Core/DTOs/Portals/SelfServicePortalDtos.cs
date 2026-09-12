namespace EduOS.Core.DTOs.Portals;

public sealed class PortalStudentDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Roll { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int AcademicYearId { get; set; }
    public int ClassId { get; set; }
    public int SectionId { get; set; }
}

public sealed class PortalAttendanceDto
{
    public DateTime Date { get; set; }
    public string Status { get; set; } = string.Empty;
    public TimeSpan? InTime { get; set; }
    public TimeSpan? OutTime { get; set; }
    public string? Remarks { get; set; }
}

public sealed class PortalResultDto
{
    public int ExamId { get; set; }
    public string ExamName { get; set; } = string.Empty;
    public decimal TotalMark { get; set; }
    public decimal TotalFullMark { get; set; }
    public decimal Percentage { get; set; }
    public decimal GPA { get; set; }
    public string? Grade { get; set; }
    public int? Position { get; set; }
    public bool IsPassed { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
}

public sealed class PortalInvoiceDto
{
    public Guid Reference { get; set; }
    public string InvoiceNo { get; set; } = string.Empty;
    public string Month { get; set; } = string.Empty;
    public int Year { get; set; }
    public decimal BilledAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal DueAmount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime DueDate { get; set; }
}

public sealed class PortalPaymentDto
{
    public Guid Reference { get; set; }
    public string ReceiptNo { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime PaymentDate { get; set; }
}

public sealed class PortalFeeLedgerDto
{
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalDue { get; set; }
    public List<PortalInvoiceDto> Invoices { get; set; } = new();
    public List<PortalPaymentDto> Payments { get; set; } = new();
}
