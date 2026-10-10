namespace EduOS.Core.DTOs.Finance;

/// <summary>Idempotent bulk invoice generation for one batch and one configured fee structure.</summary>
public sealed class GenerateStudentInvoiceBatchRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Range(1, long.MaxValue)] public long AcademicBatchId { get; set; }
    [Range(1, long.MaxValue)] public long FeeStructureId { get; set; }
    [Range(1, 12)] public int Month { get; set; }
    [Range(2000, 2200)] public int Year { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly DueDate { get; set; }
}

public sealed class InvoiceBatchResultDto
{
    public Guid ClientRequestId { get; set; }
    public int Generated { get; set; }
    public int Existing { get; set; }
    public int Failed { get; set; }
}

/// <summary>Calculated report only; invoice and payment lines are retrieved with separate paged queries.</summary>
public sealed class StudentLedgerDto
{
    public Guid StudentReference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string StudentName { get; set; } = string.Empty;
    public decimal TotalBilled { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal TotalDue { get; set; }
}

public sealed class FeeOptionDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public long? AcademicYearId { get; set; }
    public long? AcademicLevelId { get; set; }
    public long? CampusId { get; set; }
    public long? AcademicBatchId { get; set; }
    public DateOnly? EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
}

public sealed class FeeBillingOptionsDto
{
    public IReadOnlyList<FeeOptionDto> AcademicYears { get; set; } = Array.Empty<FeeOptionDto>();
    public IReadOnlyList<FeeOptionDto> AcademicLevels { get; set; } = Array.Empty<FeeOptionDto>();
    public IReadOnlyList<FeeOptionDto> AcademicBatches { get; set; } = Array.Empty<FeeOptionDto>();
    public IReadOnlyList<FeeOptionDto> FeeHeads { get; set; } = Array.Empty<FeeOptionDto>();
    public IReadOnlyList<FeeOptionDto> Campuses { get; set; } = Array.Empty<FeeOptionDto>();
    public IReadOnlyList<FeeOptionDto> FeeStructures { get; set; } = Array.Empty<FeeOptionDto>();
}

public sealed class FeeStudentOptionDto
{
    public Guid Reference { get; set; }
    public string StudentCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}
