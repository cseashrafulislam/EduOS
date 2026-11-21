using EduOS.AI;
using System.Globalization;
using EduOS.Core.Common;
using EduOS.Core.Interfaces.IServices;

namespace EduOS.Service.Services.AI;

public sealed record AiInstitutionMetricAnswer(
    string Key, string Definition, string Source, string Scope, string Unit,
    string Value, DateOnly PeriodFrom, DateOnly PeriodTo, DateTime GeneratedAtUtc);

/// <summary>All values originate from the existing authorized Central Institution MIS.</summary>
public sealed class AiInstitutionMetricService(ICentralInstitutionMisService mis)
{
    public async Task<ApiResponse<AiInstitutionMetricAnswer>> AskAsync(
        string? question, DateOnly fromDate, DateOnly toDate, CancellationToken ct)
    {
        var metricKey = InstitutionMisQuestionPlanner.Parse(question);
        if (metricKey is null)
            return ApiResponse<AiInstitutionMetricAnswer>.ErrorResponse(
                "Unsupported question. Choose one exact metric from the suggestions.", 400);
        if (fromDate == default || toDate == default || toDate < fromDate ||
            toDate.DayNumber - fromDate.DayNumber >= 366)
            return ApiResponse<AiInstitutionMetricAnswer>.ErrorResponse(
                "Select a valid MIS period of at most 366 days.", 400);
        var report = await mis.GetAsync(fromDate, toDate, ct);
        if (!report.Success || report.Data is null)
            return ApiResponse<AiInstitutionMetricAnswer>.ErrorResponse(
                report.Message ?? "Institution MIS unavailable.", report.StatusCode);
        var metric = report.Data.Metrics.SingleOrDefault(x =>
            string.Equals(x.Key, metricKey, StringComparison.Ordinal));
        if (metric is null || !metric.Available || !metric.Value.HasValue)
            return ApiResponse<AiInstitutionMetricAnswer>.ErrorResponse(
                "This metric is unavailable for the current institution subscription.", 403);
        return ApiResponse<AiInstitutionMetricAnswer>.SuccessResponse(new(
            metric.Key, metric.Definition, metric.Source, metric.Scope,
            metric.Unit, metric.Value.Value.ToString(CultureInfo.InvariantCulture), report.Data.PeriodFrom,
            report.Data.PeriodTo, report.Data.GeneratedAtUtc));
    }
}
