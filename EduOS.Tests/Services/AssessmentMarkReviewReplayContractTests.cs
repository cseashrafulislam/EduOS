using FluentAssertions;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AssessmentMarkReviewReplayContractTests
{
    [Fact]
    public void Review_idempotency_is_bound_to_actor_payload_and_latest_event()
    {
        var path = "EduOS.Service/Services/Assessment/AssessmentAdministrationService.MarkReviews.cs";
        string source = "";
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(file)) { source = File.ReadAllText(file); break; }
        }
        source.Should().NotBeEmpty();
        source.Should().Contain("previousRequest.ActorUserId != _user.UserId");
        source.Should().Contain("previousRequest.Reason, Trim(request.Reason)");
        source.Should().Contain("latestId != previousRequest.Id");
        source.Should().Contain("Request key already belongs to a different review decision.");
    }
}
