namespace EduOS.Core.DTOs.Students;

/// <summary>Only a tenant-authorized route is exposed; never a private storage key.</summary>
public sealed class StudentPhotoDto
{
    public string PhotoUrl { get; set; } = string.Empty;
    public string RowVersion { get; set; } = string.Empty;
}
