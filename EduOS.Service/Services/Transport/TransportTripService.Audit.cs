using EduOS.Core.Common;
using EduOS.Core.DTOs.Transport;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text;

namespace EduOS.Service.Services.Transport;

/// <summary>
/// Principal/tenant-admin operational audit of all frozen-trip attendance events.
/// The register is append-only readback, not a substitute for guardian-safe history.
/// </summary>
public sealed partial class TransportTripService
{
    private bool CanAudit => CanManage &&
        (_user.IsTenantAdmin || _user.IsInRole("Principal"));

    private IQueryable<TransportTripAuditRowDto> AuditEvents(long tripId, long tenant) =>
        from rider in _db.TransportTripRiders.AsNoTracking()
        join attendance in _db.TransportTripAttendanceEvents.AsNoTracking()
            on rider.Id equals attendance.TransportTripRiderId
        where rider.TenantId == tenant && !rider.IsDeleted &&
              rider.TransportTripId == tripId &&
              attendance.TenantId == tenant && !attendance.IsDeleted
        select new TransportTripAuditRowDto
        {
            StudentCode = rider.StudentCodeSnapshot,
            Sequence = attendance.Sequence,
            EventType = attendance.EventType,
            IsCorrection = attendance.CorrectsEventId != null,
            CorrectedFrom = _db.TransportTripAttendanceEvents.AsNoTracking()
                .Where(previous => previous.TenantId == tenant && !previous.IsDeleted &&
                    previous.Id == attendance.CorrectsEventId &&
                    previous.TransportTripRiderId == rider.Id)
                .Select(previous => previous.EventType).FirstOrDefault(),
            CorrectionReason = attendance.CorrectionReason,
            OccurredAtUtc = attendance.OccurredAt,
            RecordedByUserId = attendance.CreatedBy
        };

    private sealed class AuditTripScope
    {
        public TransportTripRowDto Header { get; init; } = new();
        public long Id { get; init; }
    }

    private async Task<ApiResponse<AuditTripScope>> AuditTripAsync(
        Guid reference, CancellationToken ct)
    {
        if (!CanAudit)
            return ApiResponse<AuditTripScope>.ErrorResponse(
                "Principal or tenant administrator required.", 403);
        var manifest = await GetManifestReportAsync(reference, ct);
        if (!manifest.Success || manifest.Data == null)
            return ApiResponse<AuditTripScope>.ErrorResponse(
                manifest.Message, manifest.StatusCode);
        var id = await _db.TransportTrips.AsNoTracking()
            .Where(x => x.TenantId == _user.TenantId && !x.IsDeleted &&
                x.PublicId == reference && (x.State == "Dispatched" || x.State == "Completed"))
            .Select(x => x.Id).SingleOrDefaultAsync(ct);
        if (id == 0)
            return ApiResponse<AuditTripScope>.ErrorResponse(
                "Trip changed; reload.", 409);
        return ApiResponse<AuditTripScope>.SuccessResponse(
            new AuditTripScope { Header = manifest.Data.Trip, Id = id });
    }

    public async Task<ApiResponse<PagedResult<TransportTripAuditRowDto>>> GetAttendanceAuditAsync(
        Guid reference, int page, int pageSize, CancellationToken ct = default)
    {
        if (page < 1 || pageSize is < 1 or > 100 ||
            (long)(page - 1) * pageSize > int.MaxValue)
            return ApiResponse<PagedResult<TransportTripAuditRowDto>>.ErrorResponse("Invalid audit paging.");
        var trip = await AuditTripAsync(reference, ct);
        if (!trip.Success)
            return ApiResponse<PagedResult<TransportTripAuditRowDto>>.ErrorResponse(
                trip.Message, trip.StatusCode);
        var events = AuditEvents(trip.Data!.Id, _user.TenantId);
        var count = await events.CountAsync(ct);
        if (count > 6000)
            return ApiResponse<PagedResult<TransportTripAuditRowDto>>.ErrorResponse(
                "Attendance audit exceeds safe bounds.", 409);
        var rows = await events.OrderBy(x => x.StudentCode).ThenBy(x => x.Sequence)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return ApiResponse<PagedResult<TransportTripAuditRowDto>>.SuccessResponse(
            new PagedResult<TransportTripAuditRowDto>
            { Items = rows, TotalCount = count, Page = page, PageSize = pageSize });
    }

    public async Task<ApiResponse<TransportTripAuditReportDto>> GetAttendanceAuditReportAsync(
        Guid reference, CancellationToken ct = default)
    {
        var trip = await AuditTripAsync(reference, ct);
        if (!trip.Success)
            return ApiResponse<TransportTripAuditReportDto>.ErrorResponse(
                trip.Message, trip.StatusCode);
        var events = await AuditEvents(trip.Data!.Id, _user.TenantId)
            .OrderBy(x => x.StudentCode).ThenBy(x => x.Sequence)
            .Take(6001).ToListAsync(ct);
        if (events.Count > 6000 ||
            events.GroupBy(x => x.StudentCode, StringComparer.Ordinal)
                .Any(group => group.Count() > 12 ||
                    group.Select(x => x.Sequence).Distinct().Count() != group.Count()) ||
            events.Any(x => x.IsCorrection &&
                (string.IsNullOrWhiteSpace(x.CorrectionReason) ||
                 string.IsNullOrWhiteSpace(x.CorrectedFrom))))
            return ApiResponse<TransportTripAuditReportDto>.ErrorResponse(
                "Attendance audit integrity conflict.", 409);
        return ApiResponse<TransportTripAuditReportDto>.SuccessResponse(
            new TransportTripAuditReportDto
            {
                Trip = trip.Data!.Header, Events = events,
                GeneratedAtUtc = _clock.GetUtcNow().UtcDateTime
            });
    }

    public async Task<ApiResponse<string>> ExportAttendanceAuditCsvAsync(
        Guid reference, CancellationToken ct = default)
    {
        var report = await GetAttendanceAuditReportAsync(reference, ct);
        if (!report.Success || report.Data == null)
            return ApiResponse<string>.ErrorResponse(report.Message, report.StatusCode);
        var csv = new StringBuilder(
            "\uFEFFStudent code,Sequence,Event,Corrected from,Correction reason,Occurred at UTC,Recorded by user ID\r\n");
        foreach (var row in report.Data.Events)
            csv.Append(Escape(row.StudentCode)).Append(',')
                .Append(row.Sequence.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Escape(row.EventType)).Append(',')
                .Append(Escape(row.CorrectedFrom)).Append(',')
                .Append(Escape(row.CorrectionReason)).Append(',')
                .Append(Escape(row.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Escape(row.RecordedByUserId?.ToString(CultureInfo.InvariantCulture)))
                .Append("\r\n");
        return ApiResponse<string>.SuccessResponse(csv.ToString());
    }
}
