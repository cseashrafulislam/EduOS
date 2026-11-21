namespace EduOS.AI;

/// <summary>
/// Only reviewed, code-owned capabilities can be disabled/enabled by an institution.
/// A tenant cannot define arbitrary agent code, tools, SQL or prompts via settings.
/// Adding a capability requires an app release and security review, not another column.
/// </summary>
public sealed record TenantAiAgentDescriptor(string Code, string Title, string Description);

public static class TenantAiAgentRegistry
{
    public static readonly IReadOnlyList<TenantAiAgentDescriptor> Controllable =
        new[]
        {
            new TenantAiAgentDescriptor("general", "General Q&A",
                "Optional external explanation only when the verified local MIS has no recognized answer."),
            new TenantAiAgentDescriptor("teacher", "Teacher Copilot",
                "Draft lesson plans, worksheets and practice questions for authorized educators."),
            new TenantAiAgentDescriptor("management", "Management Interpretation",
                "Explain already authorized, aggregated attendance metrics without business writes.")
        };

    public static bool Supports(string? code) =>
        code != null && Controllable.Any(x => string.Equals(x.Code, code, StringComparison.Ordinal));
}
