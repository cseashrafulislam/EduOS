using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using EduOS.Core.Common;
using Microsoft.Extensions.Logging;

namespace EduOS.Service.Services.Students;

public sealed partial class StudentImportExecutionService
{
    /// <summary>
    /// Export only source windows not yet committed for this exact file and mapping.
    /// Corrected rows become a new import source and must pass fresh validation.
    /// Existing students, guardians and enrollment history are never overwritten.
    /// </summary>
    public async Task<ApiResponse<byte[]>> ExportPendingCorrectionsAsync(Stream content, string extension,
        IReadOnlyDictionary<string, string> mapping, string mappingJson,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return ApiResponse<byte[]>.ErrorResponse("Student import access denied.", 403);
        if (content == null || !content.CanRead || !content.CanSeek ||
            mapping == null || string.IsNullOrWhiteSpace(mappingJson))
            return ApiResponse<byte[]>.ErrorResponse("Valid source file and column mapping are required.", 400);
        try
        {
            using var buffer = new MemoryStream();
            content.Position = 0;
            await content.CopyToAsync(buffer, cancellationToken);
            if (buffer.Length is <= 0 or > 6291456)
                return ApiResponse<byte[]>.ErrorResponse("Source file must be at most 6 MB.", 413);
            var bytes = buffer.ToArray();
            var rows = ReadRows(bytes, extension, mapping, 5000);
            if (rows.Count == 0)
                return ApiResponse<byte[]>.ErrorResponse("Source file has no student rows.", 400);

            // The original JSON bytes are part of the browser's deterministic request IDs.
            var fileHash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var mappingHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(mappingJson)))
                .ToLowerInvariant();
            var status = await GetJobStatusAsync(fileHash, mappingHash, rows.Count, cancellationToken);
            if (!status.Success || status.Data == null)
                return ApiResponse<byte[]>.ErrorResponse(status.Message, status.StatusCode);
            if (status.Data.RequiresReview)
                return ApiResponse<byte[]>.ErrorResponse(
                    "Committed import audit rows require review before exporting pending corrections.", 409);
            if (status.Data.IsComplete)
                return ApiResponse<byte[]>.ErrorResponse("Every source row is already committed.", 409);

            var committed = status.Data.Batches.Select(x => x.RowOffset).ToHashSet();
            var pending = rows.Where(row => !committed.Contains(
                ((row.RowNumber - 2) / MaxBatchRows) * MaxBatchRows)).ToList();
            if (pending.Count == 0)
                return ApiResponse<byte[]>.ErrorResponse("No uncommitted rows remain.", 409);

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("Pending corrections");
            sheet.Cell(1, 1).SetValue("SourceRowNumber");
            for (var col = 0; col < CanonicalFields.Length; col++)
                sheet.Cell(1, col + 2).SetValue(CanonicalFields[col]);
            sheet.Range(1, 1, 1, CanonicalFields.Length + 1).Style.Font.Bold = true;
            sheet.SheetView.FreezeRows(1);
            for (var i = 0; i < pending.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var row = pending[i];
                sheet.Cell(i + 2, 1).SetValue(row.RowNumber);
                var values = new[]
                {
                    row.SourceKey, row.StudentCode, row.FullName, row.CampusId,
                    row.BatchId.ToString(CultureInfo.InvariantCulture), row.RollNo,
                    row.DateOfBirth, row.GuardianName, row.GuardianPhone, row.GuardianSourceKey
                };
                for (var col = 0; col < values.Length; col++)
                    sheet.Cell(i + 2, col + 2).SetValue(values[col]);
            }
            using var output = new MemoryStream();
            workbook.SaveAs(output);
            if (output.Length > 8 * 1024 * 1024)
                return ApiResponse<byte[]>.ErrorResponse("Correction export exceeds the size limit.", 413);
            return ApiResponse<byte[]>.SuccessResponse(output.ToArray(),
                "Uncommitted rows exported. Re-preview edited rows as a new source file.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return ApiResponse<byte[]>.ErrorResponse("Invalid source file or mapping: " + ex.Message, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import pending-correction export failed for tenant {TenantId}",
                _user.TenantId);
            return ApiResponse<byte[]>.ErrorResponse("Unable to export pending student rows.", 500);
        }
    }
}
