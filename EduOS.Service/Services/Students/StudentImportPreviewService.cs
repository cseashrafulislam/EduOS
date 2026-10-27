using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Students;
using EduOS.Core.Entities.Academic;
using EduOS.Core.Entities.SaaS;
using EduOS.Core.Entities.Students;
using EduOS.Core.Entities.System;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IRepositories;
using EduOS.Core.Interfaces.IServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;

namespace EduOS.Service.Services.Students;

/// <summary>
/// Read-only, bounded preflight. Import execution must separately revalidate all rules
/// inside a serializable, idempotent write transaction; a preview never reserves seats.
/// </summary>
public sealed class StudentImportPreviewService : IStudentImportPreviewService
{
    private const int MaxRows = 5000;
    private const int MaxColumns = 32;
    private const int MaxReportedErrors = 100;
    private static readonly string[] RequiredFields =
        ["SourceKey", "StudentCode", "FullName", "CampusId", "AcademicBatchId", "RollNo"];
    private static readonly string[] OptionalFields =
        ["DateOfBirth", "GuardianName", "GuardianPhone"];
    private readonly IGenericRepository<Student> _students;
    private readonly IGenericRepository<StudentEnrollment> _enrollments;
    private readonly IGenericRepository<AcademicBatch> _batches;
    private readonly IGenericRepository<Campus> _campuses;
    private readonly IGenericRepository<ImportLog> _imports;
    private readonly IGenericRepository<ImportLogItem> _items;
    private readonly ICurrentUserService _user;
    private readonly ILogger<StudentImportPreviewService> _logger;

    public StudentImportPreviewService(
        IGenericRepository<Student> students,
        IGenericRepository<StudentEnrollment> enrollments,
        IGenericRepository<AcademicBatch> batches,
        IGenericRepository<Campus> campuses,
        IGenericRepository<ImportLog> imports,
        IGenericRepository<ImportLogItem> items,
        ICurrentUserService user,
        ILogger<StudentImportPreviewService> logger)
    {
        _students = students; _enrollments = enrollments; _batches = batches;
        _campuses = campuses; _imports = imports; _items = items;
        _user = user; _logger = logger;
    }

    public Task<ApiResponse<IReadOnlyList<string>>> GetHeadersAsync(
        Stream content, string extension, CancellationToken cancellationToken = default)
    {
        if (!CanManage())
            return Task.FromResult(ApiResponse<IReadOnlyList<string>>.ErrorResponse("Student import access denied.", 403));
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = ReadSheet(content, extension, headersOnly: true, cancellationToken);
            return Task.FromResult(ApiResponse<IReadOnlyList<string>>.SuccessResponse(sheet.Headers));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return Task.FromResult(ApiResponse<IReadOnlyList<string>>.ErrorResponse("Invalid CSV/XLSX headers: " + ex.Message, 400));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import header inspection failed for tenant {TenantId}", _user.TenantId);
            return Task.FromResult(ApiResponse<IReadOnlyList<string>>.ErrorResponse("Unable to inspect the import file.", 500));
        }
    }

    public async Task<ApiResponse<StudentImportPreviewDto>> PreviewAsync(
        Stream content, string extension, IReadOnlyDictionary<string, string> mapping,
        CancellationToken cancellationToken = default)
    {
        if (!CanManage()) return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Student import access denied.", 403);
        if (mapping == null) return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Column mapping is required.", 400);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheet = ReadSheet(content, extension, headersOnly: false, cancellationToken);
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in mapping)
            {
                if (!RequiredFields.Contains(pair.Key, StringComparer.OrdinalIgnoreCase) &&
                    !OptionalFields.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
                    return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Unsupported import mapping field.", 400);
                var index = sheet.Headers.FindIndex(x => string.Equals(x, pair.Value?.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index < 0 || !columns.TryAdd(pair.Key, index))
                    return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Invalid or repeated column mapping.", 400);
            }
            if (RequiredFields.Any(x => !columns.ContainsKey(x)) ||
                columns.Values.Distinct().Count() != columns.Count)
                return ApiResponse<StudentImportPreviewDto>.ErrorResponse("All required fields need distinct mapped columns.", 400);
            if (sheet.Rows.Count == 0)
                return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Import file has no student rows.", 400);

            string Cell(string[] row, string field) =>
                columns.TryGetValue(field, out var i) && i < row.Length ? row[i].Trim() : string.Empty;

            var parsed = sheet.Rows.Select((cells, index) => new Candidate(
                index + 2, Cell(cells, "SourceKey"), Cell(cells, "StudentCode"),
                Cell(cells, "FullName"), Cell(cells, "CampusId"), Cell(cells, "AcademicBatchId"),
                Cell(cells, "RollNo"), Cell(cells, "DateOfBirth"),
                Cell(cells, "GuardianName"), Cell(cells, "GuardianPhone"))).ToList();
            var tenant = _user.TenantId;
            var batchIds = parsed.Select(x => ParseId(x.BatchId)).Where(x => x > 0).Distinct().ToArray();
            var campusIds = parsed.Select(x => ParseId(x.CampusId)).Where(x => x > 0).Distinct().ToArray();
            var batches = await _batches.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive && batchIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, cancellationToken);
            var campuses = await _campuses.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsActive && campusIds.Contains(x.Id))
                .Select(x => x.Id).ToListAsync(cancellationToken);
            var allowedCampuses = campuses.ToHashSet();
            var capacityRows = await _enrollments.GetQueryable().AsNoTracking()
                .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                    x.State == EnrollmentState.Active && batchIds.Contains(x.AcademicBatchId))
                .GroupBy(x => x.AcademicBatchId)
                .Select(g => new { BatchId = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);
            var occupied = capacityRows.ToDictionary(x => x.BatchId, x => x.Count);

            var existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var codes = parsed.Select(x => x.StudentCode).Where(x => x.Length is > 0 and <= 50)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var chunk in codes.Chunk(200))
            {
                var found = await _students.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && chunk.Contains(x.StudentCode))
                    .Select(x => x.StudentCode).ToListAsync(cancellationToken);
                existingCodes.UnionWith(found);
            }

            var existingRolls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in parsed.Where(x => ParseId(x.BatchId) > 0)
                .GroupBy(x => ParseId(x.BatchId)))
            {
                var batchId = group.Key;
                foreach (var chunk in group.Select(x => x.RollNo).Where(x => x.Length is > 0 and <= 50)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Chunk(200))
                {
                    var found = await _enrollments.GetQueryable().AsNoTracking()
                        .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsCurrent &&
                            x.State == EnrollmentState.Active && x.AcademicBatchId == batchId &&
                            chunk.Contains(x.RollNo))
                        .Select(x => x.RollNo).ToListAsync(cancellationToken);
                    existingRolls.UnionWith(found.Select(x => RollKey(batchId, x)));
                }
            }

            var existingSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sources = parsed.Select(x => x.SourceKey).Where(x => x.Length is > 0 and <= 100)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            foreach (var chunk in sources.Chunk(200))
            {
                var found = await _items.GetQueryable().AsNoTracking()
                    .Where(x => x.TenantId == tenant && !x.IsDeleted && x.IsSuccess &&
                        x.BusinessKey != null && chunk.Contains(x.BusinessKey) &&
                        _imports.GetQueryable().Any(log => log.TenantId == tenant &&
                            !log.IsDeleted && log.Id == x.ImportLogId && log.ImportType == "Student"))
                    .Select(x => x.BusinessKey!).ToListAsync(cancellationToken);
                existingSources.UnionWith(found);
            }

            var seenSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenRolls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var planned = new Dictionary<long, int>();
            var result = new StudentImportPreviewDto { TotalRows = parsed.Count };
            foreach (var row in parsed)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var issues = new List<string>();
                Check(row.SourceKey, 100, "Source key", issues);
                Check(row.StudentCode, 50, "Student code", issues);
                Check(row.FullName, 200, "Full name", issues);
                Check(row.RollNo, 50, "Roll number", issues);
                if (row.GuardianName.Length > 200 || row.GuardianPhone.Length > 30)
                    issues.Add("Guardian name/phone exceeds supported length.");
                if (row.GuardianPhone.Length > 0 && row.GuardianName.Length == 0)
                    issues.Add("Guardian name is required when a guardian phone is supplied.");
                if (row.DateOfBirth.Length > 0 &&
                    (!DateOnly.TryParseExact(row.DateOfBirth, "yyyy-MM-dd",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var birth) ||
                     birth > DateOnly.FromDateTime(DateTime.UtcNow)))
                    issues.Add("Date of birth must be yyyy-MM-dd and not in the future.");

                var campusId = ParseId(row.CampusId);
                var batchId = ParseId(row.BatchId);
                if (!allowedCampuses.Contains(campusId))
                    issues.Add("Campus is not available in this institution.");
                if (!batches.TryGetValue(batchId, out var batch) || batch.CampusId != campusId)
                    issues.Add("Batch is not active or does not belong to the selected campus.");
                if (row.SourceKey.Length > 0 && (!seenSources.Add(row.SourceKey) || existingSources.Contains(row.SourceKey)))
                    issues.Add("Source key is duplicated in this file or a previous student import.");
                if (row.StudentCode.Length > 0 && (!seenCodes.Add(row.StudentCode) || existingCodes.Contains(row.StudentCode)))
                    issues.Add("Student code already exists or is repeated in this file.");
                var rollKey = RollKey(batchId, row.RollNo);
                if (row.RollNo.Length > 0 && (!seenRolls.Add(rollKey) || existingRolls.Contains(rollKey)))
                    issues.Add("Current roll already exists or is repeated for this batch.");
                if (batch != null && batch.CampusId == campusId)
                {
                    var count = occupied.GetValueOrDefault(batchId) + planned.GetValueOrDefault(batchId);
                    if (batch.Capacity <= count)
                        issues.Add("Batch capacity would be exceeded.");
                }

                if (issues.Count == 0)
                {
                    result.ValidRows++;
                    planned[batchId] = planned.GetValueOrDefault(batchId) + 1;
                }
                else
                {
                    result.InvalidRows++;
                    if (result.Errors.Count < MaxReportedErrors)
                        result.Errors.Add(new StudentImportRowIssueDto
                        {
                            RowNumber = row.RowNumber,
                            SourceKey = row.SourceKey,
                            StudentCode = row.StudentCode,
                            Issues = issues
                        });
                }
            }
            result.HasMoreErrors = result.InvalidRows > result.Errors.Count;
            return ApiResponse<StudentImportPreviewDto>.SuccessResponse(result,
                "Read-only preview completed. No student, person, guardian or enrollment was created.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or ArgumentException)
        {
            return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Invalid import file: " + ex.Message, 400);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Student import preview failed for tenant {TenantId}", _user.TenantId);
            return ApiResponse<StudentImportPreviewDto>.ErrorResponse("Unable to preview student import.", 500);
        }
    }

    private bool CanManage() => _user.IsAuthenticated && _user.TenantId > 0 && _user.IsTenantAdmin;
    private static long ParseId(string value) => long.TryParse(value, NumberStyles.None,
        CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : 0;
    private static string RollKey(long batchId, string roll) => batchId.ToString(CultureInfo.InvariantCulture) + ":" + roll;
    private static void Check(string value, int limit, string field, List<string> issues)
    {
        if (value.Length == 0 || value.Length > limit)
            issues.Add(field + " is required and must be at most " + limit + " characters.");
    }

    private static Sheet ReadSheet(Stream content, string extension, bool headersOnly, CancellationToken token)
    {
        if (!content.CanRead || !content.CanSeek) throw new FormatException("File must be readable and seekable.");
        content.Position = 0;
        var rows = new List<string[]>();
        string[] headers;
        if (string.Equals(extension, ".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(content, new UTF8Encoding(false, true),
                detectEncodingFromByteOrderMarks: true, leaveOpen: true);
            using var parser = new TextFieldParser(reader)
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = true
            };
            parser.SetDelimiters(",");
            headers = parser.ReadFields() ?? [];
            if (!headersOnly)
            {
                while (!parser.EndOfData)
                {
                    token.ThrowIfCancellationRequested();
                    if (rows.Count == MaxRows) throw new FormatException("Maximum 5,000 rows per preview. Split larger imports into bounded files.");
                    var fields = parser.ReadFields() ?? [];
                    if (fields.Length != headers.Length) throw new FormatException("CSV row has a different number of columns.");
                    rows.Add(fields);
                }
            }
        }
        else if (string.Equals(extension, ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var workbook = new XLWorkbook(content);
            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new FormatException("Workbook has no worksheet.");
            var last = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
            if (last > MaxColumns) throw new FormatException("Too many columns.");
            headers = sheet.Row(1).Cells(1, Math.Max(1, last)).Select(x => SafeCell(x)).ToArray();
            if (!headersOnly)
            {
                var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 1;
                if (lastRow > MaxRows + 1) throw new FormatException("Maximum 5,000 rows per preview. Split larger imports into bounded files.");
                for (var row = 2; row <= lastRow; row++)
                {
                    token.ThrowIfCancellationRequested();
                    rows.Add(sheet.Row(row).Cells(1, last).Select(x => SafeCell(x)).ToArray());
                }
            }
        }
        else throw new FormatException("Only .csv and .xlsx files are accepted.");

        headers = headers.Select(x => x.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Length is < 1 or > MaxColumns ||
            headers.Any(string.IsNullOrWhiteSpace) ||
            headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length)
            throw new FormatException("Headers must be unique, non-empty and at most 32 columns.");
        return new Sheet(headers.ToList(), rows);
    }

    private static string SafeCell(IXLCell cell)
    {
        if (cell.HasFormula) throw new FormatException("Spreadsheet formulas are not accepted in import files.");
        return cell.GetFormattedString().Trim();
    }

    private sealed record Sheet(List<string> Headers, List<string[]> Rows);
    private sealed record Candidate(int RowNumber, string SourceKey, string StudentCode, string FullName,
        string CampusId, string BatchId, string RollNo, string DateOfBirth,
        string GuardianName, string GuardianPhone);
}
