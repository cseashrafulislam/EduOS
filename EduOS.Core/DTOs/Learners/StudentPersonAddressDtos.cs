using System.ComponentModel.DataAnnotations;

namespace EduOS.Core.DTOs.Learners;

public sealed class StudentPersonAddressDto
{
    public string AddressTypeCode { get; set; } = string.Empty;
    public string? AddressLine1 { get; set; }
    public string? AddressLine2 { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public string? DivisionOrState { get; set; }
    public string? PostalCode { get; set; }
    public string CountryCode { get; set; } = "BD";
    public string RowVersion { get; set; } = string.Empty;
}

public sealed class SaveStudentPersonAddressRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required, StringLength(500, MinimumLength = 3)] public string AddressLine1 { get; set; } = string.Empty;
    [StringLength(500)] public string? AddressLine2 { get; set; }
    [StringLength(100)] public string? City { get; set; }
    [StringLength(100)] public string? District { get; set; }
    [StringLength(100)] public string? DivisionOrState { get; set; }
    [StringLength(30)] public string? PostalCode { get; set; }
    [Required, RegularExpression("^[A-Z]{2}$")] public string CountryCode { get; set; } = "BD";
    public string RowVersion { get; set; } = string.Empty;
}
