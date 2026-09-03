using EduOS.Core.Settings;
using EduOS.Service.Services.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace EduOS.Tests.Services;

public class MfaChallengeServiceTests
{
    [Fact]
    public void Challenge_round_trip_preserves_only_required_session_state()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 4, 1, 0, 0, TimeSpan.Zero));
        var service = CreateService(time);

        var token = service.Create(42, "security-stamp", true);
        var valid = service.TryRead(token, out var challenge);

        valid.Should().BeTrue();
        challenge.UserId.Should().Be(42);
        challenge.SecurityStamp.Should().Be("security-stamp");
        challenge.RememberMe.Should().BeTrue();
        challenge.ExpiresAtUtc.Should().Be(challenge.IssuedAtUtc.AddMinutes(5));
        token.Should().NotContain("security-stamp");
    }

    [Fact]
    public void Expired_or_tampered_challenge_is_rejected()
    {
        var time = new MutableTimeProvider(new DateTimeOffset(2026, 9, 4, 1, 0, 0, TimeSpan.Zero));
        var service = CreateService(time);
        var token = service.Create(42, "security-stamp", false);

        var replacement = token[0] == 'A' ? 'B' : 'A';
        var tampered = replacement + token[1..];
        service.TryRead(tampered, out _).Should().BeFalse();

        time.Advance(TimeSpan.FromMinutes(6));
        service.TryRead(token, out _).Should().BeFalse();
    }

    [Fact]
    public void Unsafe_challenge_lifetime_configuration_fails_closed()
    {
        var settings = Options.Create(new MfaSettings { ChallengeLifetimeMinutes = 30 });
        var service = new MfaChallengeService(
            new EphemeralDataProtectionProvider(),
            settings,
            TimeProvider.System);

        var action = () => service.Create(42, "security-stamp", false);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*between 1 and 15 minutes*");
    }

    private static MfaChallengeService CreateService(TimeProvider timeProvider) =>
        new(
            new EphemeralDataProtectionProvider(),
            Options.Create(new MfaSettings { ChallengeLifetimeMinutes = 5 }),
            timeProvider);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan value) => _utcNow = _utcNow.Add(value);
    }
}
