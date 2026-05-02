using EduOS.Core.Common;
using EduOS.Core.Interfaces;
using EduOS.Service.Services.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace EduOS.Service.Helpers.Storage
{
    public interface IFileUploadService
    {
        Task<FileUploadResult> UploadAsync(IFormFile file, string folder);
        Task<bool> DeleteAsync(string fileUrl);
        Task<bool> DeleteByPathAsync(string relativePath);
        bool ValidateFile(IFormFile file);
        Task<byte[]> GetFileContentAsync(string fileUrl);
        Task<List<FileInfo>> GetFilesInFolderAsync(string folder);
        Task<long> GetFolderSizeAsync(string folder);
    }

    public class FileStorageSettings
    {
        public string BasePath { get; set; } = "wwwroot/uploads";
        public int MaxFileSizeMB { get; set; } = 5;
        public List<string> AllowedExtensions { get; set; } = new() { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx" };
        public List<string> AllowedMimeTypes { get; set; } = new()
        {
            "image/jpeg", "image/png", "application/pdf",
            "application/msword", "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
        };
        public bool EnableVirusScan { get; set; } = false;
        public bool GenerateThumbnails { get; set; } = true;
        public int MaxFilesPerFolder { get; set; } = 10000;
        public string UrlPrefix { get; set; } = "/uploads";
    }

    public class FileUploadResult
    {
        public bool Success { get; set; }
        public string FileUrl { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public string? ThumbnailUrl { get; set; }
        public DateTime UploadedAt { get; set; }
    }

    public class FileUploadService : IFileUploadService
    {
        private readonly FileStorageSettings _settings;
        private readonly ICurrentUserService _currentUser;
        private readonly ILogger<FileUploadService> _logger;
        private readonly HashSet<string> _allowedExtensions;
        private readonly HashSet<string> _allowedMimeTypes;

        public FileUploadService(
            IOptions<FileStorageSettings> settings,
            ICurrentUserService currentUser,
            ILogger<FileUploadService> logger)
        {
            _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
            _currentUser = currentUser ?? throw new ArgumentNullException(nameof(currentUser));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _allowedExtensions = new HashSet<string>(
                _settings.AllowedExtensions.Select(e => e.ToLower()),
                StringComparer.OrdinalIgnoreCase);

            _allowedMimeTypes = new HashSet<string>(
                _settings.AllowedMimeTypes,
                StringComparer.OrdinalIgnoreCase);
        }

        public async Task<FileUploadResult> UploadAsync(IFormFile file, string folder)
        {
            var result = new FileUploadResult
            {
                UploadedAt = DateTime.UtcNow,
                FileName = file?.FileName ?? string.Empty,
                FileSize = file?.Length ?? 0
            };

            try
            {
                // Validation
                if (!ValidateFile(file))
                {
                    result.Success = false;
                    result.ErrorMessage = "File validation failed. Check file type, size, and format.";
                    return result;
                }

                // Security: Scan for malicious content
                if (_settings.EnableVirusScan && !await ScanForVirusAsync(file))
                {
                    result.Success = false;
                    result.ErrorMessage = "Virus scan detected potential threat.";
                    _logger.LogWarning("Virus scan failed for file: {FileName} uploaded by user: {UserId}",
                        file.FileName, _currentUser.UserId);
                    return result;
                }

                // Generate unique filename
                var extension = Path.GetExtension(file.FileName).ToLower();
                var fileName = GenerateSecureFileName(extension);

                // Compute file hash for deduplication
                result.FileHash = await ComputeFileHashAsync(file);

                // Check if file already exists (optional deduplication)
                var existingFile = await FindDuplicateFileAsync(result.FileHash, folder);
                if (existingFile != null && _settings.GenerateThumbnails)
                {
                    result.Success = true;
                    result.FileUrl = existingFile;
                    _logger.LogDebug("Duplicate file detected. Returning existing file: {FileUrl}", existingFile);
                    return result;
                }

                // Build folder structure: tenant-{tenantId}/{folder}/{year}/{month}/
                var relativePath = BuildRelativePath(folder);
                var fullPath = Path.Combine(_settings.BasePath, relativePath);

                // Create directory if not exists
                if (!Directory.Exists(fullPath))
                {
                    Directory.CreateDirectory(fullPath);
                }

                // Check folder capacity
                var fileCount = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories).Length;
                if (fileCount >= _settings.MaxFilesPerFolder)
                {
                    throw new InvalidOperationException($"Folder has reached maximum capacity of {_settings.MaxFilesPerFolder} files");
                }

                // Save file
                var filePath = Path.Combine(fullPath, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                {
                    await file.CopyToAsync(stream);
                    await stream.FlushAsync();
                }

                // Generate thumbnail for images
                if (_settings.GenerateThumbnails && IsImageFile(extension))
                {
                    result.ThumbnailUrl = await GenerateThumbnailAsync(filePath, relativePath, fileName);
                }

                // Build URL
                result.Success = true;
                result.FileUrl = $"{_settings.UrlPrefix}/{relativePath}/{fileName}".Replace("\\", "/");

                _logger.LogInformation("File uploaded successfully: {FileUrl} by user: {UserId}, size: {Size} bytes",
                    result.FileUrl, _currentUser.UserId, file.Length);

                return result;
            }
            catch (IOException ex)
            {
                result.Success = false;
                result.ErrorMessage = $"File system error: {ex.Message}";
                _logger.LogError(ex, "IO error uploading file: {FileName}", file?.FileName);
                return result;
            }
            catch (UnauthorizedAccessException ex)
            {
                result.Success = false;
                result.ErrorMessage = "Permission denied to write file";
                _logger.LogError(ex, "Permission error uploading file: {FileName}", file?.FileName);
                return result;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Upload failed: {ex.Message}";
                _logger.LogError(ex, "Unexpected error uploading file: {FileName}", file?.FileName);
                return result;
            }
        }

        public async Task<bool> DeleteAsync(string fileUrl)
        {
            if (string.IsNullOrWhiteSpace(fileUrl))
                return false;

            try
            {
                // Extract relative path from URL
                var relativePath = ExtractRelativePath(fileUrl);
                if (string.IsNullOrEmpty(relativePath))
                    return false;

                var fullPath = Path.Combine(_settings.BasePath, relativePath);

                if (!File.Exists(fullPath))
                {
                    _logger.LogWarning("File not found for deletion: {FileUrl}", fileUrl);
                    return false;
                }

                // Delete thumbnail if exists
                var thumbnailPath = GetThumbnailPath(fullPath);
                if (File.Exists(thumbnailPath))
                {
                    await Task.Run(() => File.Delete(thumbnailPath));
                }

                // Delete main file
                await Task.Run(() => File.Delete(fullPath));

                _logger.LogInformation("File deleted successfully: {FileUrl} by user: {UserId}",
                    fileUrl, _currentUser.UserId);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file: {FileUrl}", fileUrl);
                return false;
            }
        }

        public async Task<bool> DeleteByPathAsync(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return false;

            try
            {
                var fullPath = Path.Combine(_settings.BasePath, relativePath);

                if (File.Exists(fullPath))
                {
                    await Task.Run(() => File.Delete(fullPath));
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting file by path: {RelativePath}", relativePath);
                return false;
            }
        }

        public bool ValidateFile(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                _logger.LogDebug("File validation failed: File is null or empty");
                return false;
            }

            // Check size
            var maxSizeBytes = _settings.MaxFileSizeMB * 1024L * 1024L;
            if (file.Length > maxSizeBytes)
            {
                _logger.LogDebug("File validation failed: Size {Size} exceeds limit {Limit}MB",
                    file.Length, _settings.MaxFileSizeMB);
                return false;
            }

            // Check extension
            var extension = Path.GetExtension(file.FileName).ToLower();
            if (!_allowedExtensions.Contains(extension))
            {
                _logger.LogDebug("File validation failed: Extension {Extension} not allowed", extension);
                return false;
            }

            // Check MIME type (more secure than extension)
            if (!string.IsNullOrEmpty(file.ContentType) && !_allowedMimeTypes.Contains(file.ContentType.ToLower()))
            {
                _logger.LogDebug("File validation failed: MIME type {MimeType} not allowed", file.ContentType);
                return false;
            }

            // Validate file signature (magic bytes) to prevent extension spoofing
            if (!ValidateFileSignature(file, extension))
            {
                _logger.LogWarning("File validation failed: Signature mismatch for {FileName}", file.FileName);
                return false;
            }

            return true;
        }

        public async Task<byte[]> GetFileContentAsync(string fileUrl)
        {
            var relativePath = ExtractRelativePath(fileUrl);
            if (string.IsNullOrEmpty(relativePath))
                throw new FileNotFoundException("Invalid file URL");

            var fullPath = Path.Combine(_settings.BasePath, relativePath);

            if (!File.Exists(fullPath))
                throw new FileNotFoundException($"File not found: {fileUrl}");

            return await File.ReadAllBytesAsync(fullPath);
        }

        public async Task<List<FileInfo>> GetFilesInFolderAsync(string folder)
        {
            var relativePath = BuildRelativePath(folder);
            var fullPath = Path.Combine(_settings.BasePath, relativePath);

            if (!Directory.Exists(fullPath))
                return new List<FileInfo>();

            var files = await Task.Run(() => Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f))
                .ToList());

            return files;
        }

        public async Task<long> GetFolderSizeAsync(string folder)
        {
            var files = await GetFilesInFolderAsync(folder);
            return await Task.Run(() => files.Sum(f => f.Length));
        }

        #region Private Methods

        private string BuildRelativePath(string folder)
        {
            var tenantId = _currentUser.TenantId;
            var sanitizedFolder = SanitizePathComponent(folder);
            var datePath = DateTime.UtcNow.ToString("yyyy/MM");

            return Path.Combine($"tenant-{tenantId}", sanitizedFolder, datePath);
        }

        private string GenerateSecureFileName(string extension)
        {
            using var rng = RandomNumberGenerator.Create();
            var randomBytes = new byte[16];
            rng.GetBytes(randomBytes);

            var timestamp = DateTime.UtcNow.Ticks;
            var uniqueId = Convert.ToBase64String(randomBytes)
                .Replace("/", "_")
                .Replace("+", "-")
                .TrimEnd('=');

            return $"{timestamp}_{uniqueId}{extension}";
        }

        private async Task<string> ComputeFileHashAsync(IFormFile file)
        {
            using var sha256 = SHA256.Create();
            using var stream = file.OpenReadStream();
            var hash = await sha256.ComputeHashAsync(stream);
            return Convert.ToBase64String(hash);
        }

        private async Task<string?> FindDuplicateFileAsync(string fileHash, string folder)
        {
            var relativePath = BuildRelativePath(folder);
            var fullPath = Path.Combine(_settings.BasePath, relativePath);

            if (!Directory.Exists(fullPath))
                return null;

            var files = Directory.GetFiles(fullPath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                using var sha256 = SHA256.Create();
                using var stream = File.OpenRead(file);
                var hash = await sha256.ComputeHashAsync(stream);
                var currentHash = Convert.ToBase64String(hash);

                if (currentHash == fileHash)
                {
                    var relativeFilePath = Path.GetRelativePath(_settings.BasePath, file);
                    return $"{_settings.UrlPrefix}/{relativeFilePath}".Replace("\\", "/");
                }
            }

            return null;
        }

        private bool ValidateFileSignature(IFormFile file, string extension)
        {
            using var reader = new BinaryReader(file.OpenReadStream());
            var header = reader.ReadBytes(8); // Read first 8 bytes
            file.OpenReadStream().Position = 0; // Reset stream position

            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => header[0] == 0xFF && header[1] == 0xD8,
                ".png" => header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47,
                ".pdf" => header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46,
                ".doc" => header[0] == 0xD0 && header[1] == 0xCF && header[2] == 0x11 && header[3] == 0xE0,
                ".docx" => header[0] == 0x50 && header[1] == 0x4B, // PK zip file
                _ => true
            };
        }

        private async Task<string?> GenerateThumbnailAsync(string filePath, string relativePath, string fileName)
        {
            try
            {
                // This requires SixLabors.ImageSharp or similar library
                // Simplified version - in production, use proper image processing
                var thumbFileName = $"thumb_{fileName}";
                var thumbPath = Path.Combine(Path.GetDirectoryName(filePath)!, thumbFileName);

                // Placeholder - implement actual thumbnail generation
                // await using var image = await Image.LoadAsync(filePath);
                // image.Mutate(x => x.Resize(200, 0));
                // await image.SaveAsync(thumbPath);

                return $"{_settings.UrlPrefix}/{relativePath}/{thumbFileName}".Replace("\\", "/");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to generate thumbnail for {FileName}", fileName);
                return null;
            }
        }

        private static bool IsImageFile(string extension)
        {
            return extension is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp";
        }

        private static string ExtractRelativePath(string fileUrl)
        {
            var pattern = @$"^/?(uploads/)?(.+)$";
            var match = Regex.Match(fileUrl, pattern);
            return match.Success ? match.Groups[2].Value : string.Empty;
        }

        private static string SanitizePathComponent(string component)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            return string.Join("_", component.Split(invalidChars));
        }

        private static string GetThumbnailPath(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var extension = Path.GetExtension(filePath);
            return Path.Combine(directory!, $"thumb_{fileName}{extension}");
        }

        private async Task<bool> ScanForVirusAsync(IFormFile file)
        {
            // Integration with ClamAV or Windows Defender
            // This is a placeholder - implement actual virus scanning
            await Task.Delay(10);
            return true;
        }

        #endregion
    }

    // File upload validation attribute for model binding
    public class AllowedFileExtensionsAttribute : ValidationAttribute
    {
        private readonly string[] _extensions;

        public AllowedFileExtensionsAttribute(string[] extensions)
        {
            _extensions = extensions;
        }

        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (value is IFormFile file)
            {
                var extension = Path.GetExtension(file.FileName);
                if (!_extensions.Contains(extension.ToLower()))
                {
                    return new ValidationResult($"Only {string.Join(", ", _extensions)} files are allowed.");
                }
            }
            return ValidationResult.Success;
        }
    }
}