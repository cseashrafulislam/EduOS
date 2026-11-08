using ClosedXML.Excel;
using EduOS.Core.Common;
using EduOS.Core.DTOs.Assessment;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EduOS.Service.Services.Omr;

public sealed partial class OmrWorkflowService
{
    private sealed class OmrReportRow
    {
        public long RegistrationId { get; set; }
        public string StudentCode { get; set; } = "";
        public string RollNo { get; set; } = "";
        public string StudentName { get; set; } = "";
        public decimal ComponentMarks { get; set; }
        public decimal SubjectMarks { get; set; }
        public DateTime? ReviewedAtUtc { get; set; }
        public DateTime? PostedAtUtc { get; set; }
    }

    private static readonly IReadOnlyDictionary<string, string> ExportLabels =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["RegistrationId"] = "Registration ID", ["RollNo"] = "Roll No",
            ["StudentCode"] = "Student Code", ["StudentName"] = "Student Name",
            ["ComponentMarks"] = "OMR Component Marks", ["SubjectMarks"] = "Subject Total Marks",
            ["ReviewedAt"] = "Reviewed (UTC)", ["PostedAt"] = "Posted (UTC)"
        };
    private static readonly string[] DefaultColumns =
        ["RollNo", "StudentCode", "StudentName", "ComponentMarks", "SubjectMarks", "PostedAt"];

    private static string CellText(OmrReportRow row, string column) => column switch
    {
        "RegistrationId" => row.RegistrationId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "RollNo" => row.RollNo,
        "StudentCode" => row.StudentCode,
        "StudentName" => row.StudentName,
        "ComponentMarks" => row.ComponentMarks.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        "SubjectMarks" => row.SubjectMarks.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        "ReviewedAt" => row.ReviewedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "",
        "PostedAt" => row.PostedAtUtc?.ToString("yyyy-MM-dd HH:mm 'UTC'") ?? "",
        _ => ""
    };

    /// <summary>Configurable, bounded INTERNAL board-preparation export. The canonical marks
    /// register supplies values; OMR is only the reviewed evidence and immutable lineage.</summary>
    public async Task<ApiResponse<OmrExportFileDto>> ExportAsync(OmrExportRequestDto request,
        CancellationToken ct = default)
    {
        if (!IsExamManager) return Fail<OmrExportFileDto>("Exam management permission required.", 403);
        var format = request?.Format?.Trim().ToLowerInvariant();
        if (request == null || request.TemplateReference == Guid.Empty || format is not ("xlsx" or "pdf") ||
            request.Columns == null || request.Columns.Count > ExportLabels.Count)
            return Fail<OmrExportFileDto>("Invalid OMR report parameters.");
        var columns = request.Columns.Count == 0 ? DefaultColumns : request.Columns
            .Select(x => x?.Trim() ?? "").ToArray();
        if (columns.Length == 0 || columns.Distinct(StringComparer.OrdinalIgnoreCase).Count() != columns.Length ||
            columns.Any(x => !ExportLabels.ContainsKey(x)))
            return Fail<OmrExportFileDto>("Invalid or duplicate report columns.");
        // Resolve names to canonical spelling; never use client text as a formula, path or SQL fragment.
        columns = columns.Select(x => ExportLabels.Keys.Single(k =>
            string.Equals(k, x, StringComparison.OrdinalIgnoreCase))).ToArray();
        var tenant = user.TenantId;
        var form = await db.OmrTemplates.AsNoTracking().FirstOrDefaultAsync(x =>
            x.TenantId == tenant && x.PublicId == request.TemplateReference && !x.IsDeleted, ct);
        if (form == null) return Fail<OmrExportFileDto>("OMR template not found.", 404);
        var scope = await ScopeAsync(form.AssessmentComponentId, ct);
        if (scope == null) return Fail<OmrExportFileDto>("Assessment access denied.", 403);
        var max = format == "pdf" ? 500 : 5000;
        var sheets = db.OmrSheets.AsNoTracking().Where(x => x.TenantId == tenant &&
            x.OmrTemplateId == form.Id && x.State == "Posted" && !x.IsDeleted);
        var count = await sheets.CountAsync(ct);
        if (count == 0) return Fail<OmrExportFileDto>("No posted OMR sheets are available.", 404);
        if (count > max) return Fail<OmrExportFileDto>(
            $"This {format.ToUpperInvariant()} report allows up to {max} posted students; split the source assessment before exporting.", 409);
        var rows = await (from sheet in sheets
            join registration in db.StudentSubjectRegistrations.AsNoTracking() on
                new { sheet.TenantId, RegistrationId = sheet.StudentSubjectRegistrationId } equals
                new { registration.TenantId, RegistrationId = registration.Id }
            join enrollment in db.StudentEnrollments.AsNoTracking() on
                new { registration.TenantId, EnrollmentId = registration.StudentEnrollmentId } equals
                new { enrollment.TenantId, EnrollmentId = enrollment.Id }
            join student in db.Students.AsNoTracking() on
                new { enrollment.TenantId, StudentId = enrollment.StudentId } equals
                new { student.TenantId, StudentId = student.Id }
            join mark in db.StudentAssessmentMarks.AsNoTracking() on
                new { sheet.TenantId, RegistrationId = sheet.StudentSubjectRegistrationId,
                    AssessmentSubjectId = scope.SubjectId } equals
                new { mark.TenantId, RegistrationId = mark.StudentSubjectRegistrationId,
                    mark.AssessmentSubjectId }
            join componentMark in db.StudentAssessmentComponentMarks.AsNoTracking() on
                new { mark.TenantId, StudentAssessmentMarkId = mark.Id,
                    AssessmentComponentId = form.AssessmentComponentId } equals
                new { componentMark.TenantId, componentMark.StudentAssessmentMarkId,
                    componentMark.AssessmentComponentId }
            where registration.SubjectOfferingId == scope.OfferingId &&
                enrollment.AcademicBatchId == scope.BatchId && enrollment.CampusId == scope.CampusId &&
                !registration.IsDeleted && !enrollment.IsDeleted && !student.IsDeleted &&
                !mark.IsDeleted && !componentMark.IsDeleted
            orderby enrollment.RollNo, student.StudentCode
            select new OmrReportRow
            {
                RegistrationId = registration.Id, RollNo = enrollment.RollNo,
                StudentCode = student.StudentCode, StudentName = student.FullName,
                ComponentMarks = componentMark.ObtainedMarks, SubjectMarks = mark.ObtainedMarks,
                ReviewedAtUtc = sheet.ReviewedAtUtc, PostedAtUtc = sheet.PostedAtUtc
            }).Take(max + 1).ToListAsync(ct);
        if (rows.Count != count)
            return Fail<OmrExportFileDto>("Canonical OMR marks do not reconcile with the sheet register.", 409);
        byte[] content;
        if (format == "xlsx") content = MakeExcel(form.Name, columns, rows);
        else
        {
            var license = configuration["Omr:QuestPdfLicense"];
            if (!Enum.TryParse<LicenseType>(license, true, out var selection) ||
                selection is not (LicenseType.Community or LicenseType.Professional or LicenseType.Enterprise))
                return Fail<OmrExportFileDto>("A valid QuestPDF license selection must be configured for PDF export.", 503);
            QuestPDF.Settings.License = selection;
            content = MakePdf(form.Name, columns, rows);
        }
        var result = new OmrExportFileDto
        {
            Content = content, FileName = $"EduOS-OMR-internal-{request.TemplateReference:N}.{format}",
            ContentType = format == "pdf" ? "application/pdf" :
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
        return ApiResponse<OmrExportFileDto>.SuccessResponse(result);
    }

    private static byte[] MakeExcel(string title, IReadOnlyList<string> columns,
        IReadOnlyList<OmrReportRow> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("OMR Posted Marks");
        sheet.Cell(1, 1).Value = "INTERNAL PREPARATION ONLY — NOT A BOARD SUBMISSION";
        sheet.Cell(2, 1).SetValue(SafeSpreadsheetText(title));
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(4, c + 1).Value = ExportLabels[columns[c]];
            sheet.Cell(4, c + 1).Style.Font.Bold = true;
            sheet.Column(c + 1).Width = columns[c] == "StudentName" ? 34 : 22;
        }
        for (var r = 0; r < rows.Count; r++)
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(r + 5, c + 1).SetValue(SafeSpreadsheetText(CellText(rows[r], columns[c])));
        }
        sheet.SheetView.FreezeRows(4);
        sheet.Range(4, 1, rows.Count + 4, columns.Count).SetAutoFilter();
        using var memory = new MemoryStream();
        workbook.SaveAs(memory);
        return memory.ToArray();
    }

    private static string SafeSpreadsheetText(string value) =>
        value.Length > 0 && "=+-@\t\r".Contains(value[0]) ? "'" + value : value;

    private static byte[] MakePdf(string title, IReadOnlyList<string> columns,
        IReadOnlyList<OmrReportRow> rows)
    {
        return Document.Create(doc => doc.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(20);
            page.DefaultTextStyle(style => style.FontSize(8));
            page.Header().Column(stack =>
            {
                stack.Item().Text("INTERNAL PREPARATION ONLY — NOT A BOARD SUBMISSION").Bold().FontSize(11);
                stack.Item().Text(title).FontSize(10);
            });
            page.Content().PaddingTop(12).Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    foreach (var _ in columns) definition.RelativeColumn();
                });
                table.Header(header =>
                {
                    foreach (var key in columns)
                        header.Cell().BorderBottom(1).Padding(3).Text(ExportLabels[key]).Bold();
                });
                foreach (var row in rows)
                foreach (var key in columns)
                    table.Cell().BorderBottom(0.3f).Padding(2).Text(CellText(row, key));
            });
            page.Footer().AlignRight().Text(pageNo => pageNo.CurrentPageNumber());
        })).GeneratePdf();
    }
}
