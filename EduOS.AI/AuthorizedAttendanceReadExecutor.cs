namespace EduOS.AI;

/// <summary>
/// Server-only, approved attendance read adapter. The implementation must call the canonical
/// attendance report service with the authenticated tenant/campus/record scope; never accept
/// scope from the question, transcript, or browser payload.
/// </summary>
public interface IApprovedAttendanceCountReader
{
    Task<AttendanceCountReadResult> ReadAsync(
        AttendanceCountQuerySpec query,
        AiRequestScope authenticatedScope,
        CancellationToken cancellationToken);
}

/// <summary>
/// The host must reconstruct authorization from the current authenticated principal and
/// current entitlements on EVERY invocation. A previously approved preview is not authority.
/// </summary>
public interface IAiInvocationScopeProvider
{
    Task<AiRequestScope?> GetCurrentScopeAsync(CancellationToken cancellationToken);
}

public sealed record AttendanceCountReadResult(
    long? Count,
    DateTimeOffset AsOfUtc,
    bool HasSourceData,
    string SourceReportVersion);

public enum AttendanceExecutionStatus
{
    Success,
    Denied,
    Unsupported,
    NoData,
    Failed
}

public sealed record AttendanceExecutionResult(
    AttendanceExecutionStatus Status,
    long? Count,
    DateOnly? Date,
    AttendanceCountStatus? AttendanceStatus,
    DateTimeOffset? AsOfUtc,
    string? SourceReportVersion,
    string Message)
{
    public static AttendanceExecutionResult Reject(AttendanceExecutionStatus status, string message) =>
        new(status, null, null, null, null, null, message);
}

/// <summary>
/// Model-free read boundary. No generic SQL, HTTP, model calls, or posting APIs.
/// This class must only be wired to an independently reviewed canonical report adapter.
/// </summary>
public sealed class AuthorizedAttendanceReadExecutor(
    ReviewedCapabilityRegistry registry,
    IAiInvocationScopeProvider scopeProvider,
    IApprovedAttendanceCountReader reader)
{
    public async Task<AttendanceExecutionResult> ExecuteAsync(
        CapabilityResult planned,
        CancellationToken cancellationToken = default)
    {
        if (planned is not { Decision: CapabilityDecision.Ready, Query: not null } ||
            !string.Equals(planned.CapabilityKey, "attendance.status.count", StringComparison.Ordinal) ||
            !registry.TryGet("attendance.status.count", out var capability) ||
            capability is null ||
            !string.Equals(capability.RegisteredMetricKey, "attendance.status.count.v1", StringComparison.Ordinal))
            return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Unsupported,
                "This registered attendance report is unavailable.");

        // Re-authorize at execution, never reuse the scope from planning/voice preview.
        var scope = await scopeProvider.GetCurrentScopeAsync(cancellationToken);
        if (!LocalFirstCapabilityPlanner.IsValidScope(scope) ||
            !scope!.Permissions.Contains(capability.RequiredPermission))
            return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Denied, "Access denied.");

        // Reject unreviewed enum values even if a caller constructs a QuerySpec directly.
        if (planned.Query.Status is not (AttendanceCountStatus.Present or AttendanceCountStatus.Absent))
            return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Unsupported,
                "Unsupported attendance status.");

        // The adapter is responsible for canonical row/ownership filtering, read-only execution,
        // bounded queries, and current permission checks for its own underlying report service.
        // Fail closed; do not expose SQL, internal exceptions, or sensitive source details.
        try
        {
            var data = await reader.ReadAsync(planned.Query, scope, cancellationToken);
            if (data is null || data.Count < 0 ||
                string.IsNullOrWhiteSpace(data.SourceReportVersion) ||
                data.AsOfUtc == default)
                return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Failed,
                    "The authorized report could not be verified.");
            if (!data.HasSourceData)
                return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.NoData,
                    "No attendance source data is available for the selected date.");
            if (data.Count is null)
                return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Failed,
                    "The authorized report returned no valid count.");

            return new AttendanceExecutionResult(AttendanceExecutionStatus.Success,
                data.Count, planned.Query.Date, planned.Query.Status, data.AsOfUtc,
                data.SourceReportVersion, "Attendance count from the authorized report.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return AttendanceExecutionResult.Reject(AttendanceExecutionStatus.Failed,
                "The authorized attendance report is temporarily unavailable.");
        }
    }
}
