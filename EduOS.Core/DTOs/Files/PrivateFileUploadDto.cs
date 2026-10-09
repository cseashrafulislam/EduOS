using System.IO;

namespace EduOS.Core.DTOs.Files;

/// <summary>Transient inbound upload stream. The receiving service validates size, type and content,
/// performs malware checks, and disposes the stream after processing. Never persist this DTO.</summary>
public sealed class PrivateFileUploadDto
{
    [Required, MaxLength(255)] public string FileName { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string ContentType { get; set; } = "application/octet-stream";
    [Range(1, long.MaxValue)] public long Length { get; set; }
    [Required] public Stream Content { get; set; } = Stream.Null;
}
