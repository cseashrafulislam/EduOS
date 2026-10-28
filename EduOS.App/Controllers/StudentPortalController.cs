using EduOS.App.Authorization;
using EduOS.Core.DTOs.Portals;
using EduOS.Core.Interfaces.IServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text;

namespace EduOS.App.Controllers;

[Authorize(Roles = "Student,Guardian,Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class StudentPortalController : Controller
{
    private readonly ISelfServicePortalService _portal;
    public StudentPortalController(ISelfServicePortalService portal) => _portal = portal;

    [HttpGet]
    public IActionResult Index() => View();

    [HttpGet]
    [RequireModule("EXAM")]
    public IActionResult ResultRescrutiny() => View();

    [HttpGet]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> FeeDocument(Guid studentReference, Guid documentReference,
        string kind, CancellationToken ct)
    {
        var result = await ReadFeeDocumentAsync(studentReference, documentReference, kind, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode);
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        return View(result.Data);
    }

    [HttpGet]
    [RequireModule("FINANCE")]
    public async Task<IActionResult> FeeDocumentCsv(Guid studentReference, Guid documentReference,
        string kind, CancellationToken ct)
    {
        var result = await ReadFeeDocumentAsync(studentReference, documentReference, kind, ct);
        if (!result.Success || result.Data == null) return StatusCode(result.StatusCode);
        var document = result.Data;
        var csv = new StringBuilder();
        csv.AppendLine("Document,Number,Date,Student code,Currency,Status");
        csv.AppendLine(string.Join(",", new[] { document.DocumentType, document.DocumentNumber,
            document.DocumentDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            document.StudentCode, document.CurrencyCode, document.Status }.Select(CsvCell)));
        csv.AppendLine("Description,Amount,Discount,Fine,Net");
        foreach (var line in document.Lines)
            csv.AppendLine(string.Join(",", new[] { line.Description, Money(line.Amount),
                Money(line.DiscountAmount), Money(line.FineAmount), Money(line.NetAmount) }.Select(CsvCell)));
        csv.AppendLine(string.Join(",", new[] { "Total", Money(document.Subtotal),
            Money(document.DiscountAmount), Money(document.FineAmount),
            Money(document.TotalAmount) }.Select(CsvCell)));
        Response.Headers["Cache-Control"] = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        return File(bytes, "text/csv; charset=utf-8", "fee-" + kind + "-" +
            document.Reference.ToString("N") + ".csv");
    }

    private Task<EduOS.Core.Common.ApiResponse<PortalFeeDocumentDto>> ReadFeeDocumentAsync(
        Guid studentReference, Guid documentReference, string kind, CancellationToken ct) =>
        kind == "invoice" ? _portal.GetInvoiceDocumentAsync(studentReference, documentReference, ct) :
        kind == "receipt" ? _portal.GetReceiptDocumentAsync(studentReference, documentReference, ct) :
        Task.FromResult(EduOS.Core.Common.ApiResponse<PortalFeeDocumentDto>.ErrorResponse(
            "Unknown document type.", 404));

    private static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string CsvCell(string? value)
    {
        var text = value ?? "";
        if (text.TrimStart().Length > 0 && "=+-@".Contains(text.TrimStart()[0]))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
