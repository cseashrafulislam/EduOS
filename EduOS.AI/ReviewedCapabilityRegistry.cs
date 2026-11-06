using System.Collections.ObjectModel;
using System.Globalization;

namespace EduOS.AI;

/// <summary>
/// Reviewed, in-process capability declarations. These are NOT executable plugins or SQL.
/// A host must provide authenticated server context and an approved, scoped read adapter.
/// </summary>
public sealed record CapabilityDescriptor(
    string Key,
    int Version,
    string RegisteredMetricKey,
    string RequiredPermission,
    bool Enabled = true);

public sealed record AiRequestScope(
    string TenantId,
    string CampusId,
    string UserId,
    IReadOnlySet<string> Permissions,
    bool Entitled);

public sealed record AttendanceCountQuerySpec(DateOnly Date, AttendanceCountStatus Status);

public enum AttendanceCountStatus { Present, Absent }

public enum CapabilityDecision { Ready, ClarificationRequired, Unavailable, Denied }

public sealed record CapabilityResult(
    CapabilityDecision Decision,
    string? CapabilityKey,
    AttendanceCountQuerySpec? Query,
    string? Message)
{
    public static CapabilityResult Reject(CapabilityDecision decision, string message) =>
        new(decision, null, null, message);
}

/// <summary>
/// Explicitly allowlisted capabilities, immutable after construction. No reflection,
/// dynamic assemblies, arbitrary SQL, remote providers, or user-supplied handlers.
/// </summary>
public sealed class ReviewedCapabilityRegistry
{
    private readonly IReadOnlyDictionary<string, CapabilityDescriptor> _items;

    public ReviewedCapabilityRegistry(IEnumerable<CapabilityDescriptor> reviewedCapabilities)
    {
        ArgumentNullException.ThrowIfNull(reviewedCapabilities);
        var items = new Dictionary<string, CapabilityDescriptor>(StringComparer.Ordinal);
        foreach (var item in reviewedCapabilities)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Key) ||
                string.IsNullOrWhiteSpace(item.RegisteredMetricKey) ||
                string.IsNullOrWhiteSpace(item.RequiredPermission) || item.Version < 1 ||
                !items.TryAdd(item.Key, item))
                throw new ArgumentException("Invalid or duplicate reviewed capability.");
        }
        _items = new ReadOnlyDictionary<string, CapabilityDescriptor>(items);
    }

    public bool TryGet(string key, out CapabilityDescriptor? capability)
    {
        capability = null;
        if (string.IsNullOrWhiteSpace(key) || !_items.TryGetValue(key, out var candidate) ||
            !candidate.Enabled)
            return false;
        capability = candidate;
        return true;
    }

    public static ReviewedCapabilityRegistry AttendanceOnly() => new(
    [
        new CapabilityDescriptor("attendance.status.count", 1,
            "attendance.status.count.v1", "Attendance.View")
    ]);
}

/// <summary>
/// Pure planning boundary. No database, HTTP, ML inference or credentials are accessed.
/// Scope must be reconstructed from authenticated server claims, never from a transcript.
/// </summary>
public sealed class LocalFirstCapabilityPlanner(ReviewedCapabilityRegistry registry)
{
    public CapabilityResult PlanAttendance(string? question, DateOnly localToday, AiRequestScope? scope)
    {
        if (!IsValidScope(scope))
            return CapabilityResult.Reject(CapabilityDecision.Denied, "Access denied.");

        var plan = DeterministicQuestionPlanner.Parse(question, localToday);
        if (plan.Status == PlanStatus.ClarificationRequired)
            return CapabilityResult.Reject(CapabilityDecision.ClarificationRequired,
                plan.Message ?? "Please clarify your question.");
        if (plan.Status != PlanStatus.Ready ||
            !registry.TryGet("attendance.status.count", out var capability) ||
            !string.Equals(plan.RegisteredMetricKey, capability!.RegisteredMetricKey,
                StringComparison.Ordinal))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable,
                "This question is not supported.");

        if (!scope!.Permissions.Contains(capability.RequiredPermission))
            return CapabilityResult.Reject(CapabilityDecision.Denied, "Access denied.");

        if (!plan.Slots.TryGetValue("date", out var dateValue) ||
            !DateOnly.TryParseExact(dateValue, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
            !plan.Slots.TryGetValue("status", out var statusValue))
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Invalid question slots.");

        var status = statusValue switch
        {
            "present" => AttendanceCountStatus.Present,
            "absent" => AttendanceCountStatus.Absent,
            _ => (AttendanceCountStatus?)null
        };
        if (status is null)
            return CapabilityResult.Reject(CapabilityDecision.Unavailable, "Invalid attendance status.");

        return new CapabilityResult(CapabilityDecision.Ready, capability.Key,
            new AttendanceCountQuerySpec(date, status.Value), null);
    }

    public static bool IsValidScope(AiRequestScope? scope) =>
        scope is { Entitled: true, Permissions: not null } &&
        !string.IsNullOrWhiteSpace(scope.TenantId) &&
        !string.IsNullOrWhiteSpace(scope.CampusId) &&
        !string.IsNullOrWhiteSpace(scope.UserId);
}

/// <summary>
/// Untrusted STT text is treated identically to typed text. This is only a preview;
/// it cannot post attendance, grades, fees or any other business record.
/// </summary>
public sealed record VoiceReadDraft(
    CapabilityDecision Decision,
    AttendanceCountQuerySpec? Preview,
    string Message,
    bool RequiresConfirmation);

public sealed class LocalFirstVoiceDraftService(LocalFirstCapabilityPlanner planner)
{
    public const int MaximumTranscriptLength = DeterministicQuestionPlanner.MaximumQuestionLength;

    public VoiceReadDraft PreviewAttendance(
        string? transcript, DateOnly localToday, AiRequestScope? authenticatedScope)
    {
        if (string.IsNullOrWhiteSpace(transcript) || transcript.Length > MaximumTranscriptLength)
            return new VoiceReadDraft(CapabilityDecision.Unavailable, null,
                "Transcript is empty or too long.", false);

        var result = planner.PlanAttendance(transcript, localToday, authenticatedScope);
        return result.Decision == CapabilityDecision.Ready
            ? new VoiceReadDraft(result.Decision, result.Query,
                "Review the attendance query before requesting the authorized report.", false)
            : new VoiceReadDraft(result.Decision, null,
                result.Message ?? "Unable to prepare the request.", false);
    }
}
