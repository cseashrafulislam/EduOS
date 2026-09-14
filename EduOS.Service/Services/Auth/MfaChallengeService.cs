using EduOS.Core.DTOs.Auth;
using EduOS.Core.Interfaces.IServices;
using EduOS.Core.Settings;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text.Json;

namespace EduOS.Service.Services.Auth;

public sealed class MfaChallengeService : IMfaChallengeService
{
    private const string ProtectionPurpose = "EduOS.MfaLoginChallenge.v1";
    private readonly IDataProtector _protector;
    private readonly MfaSettings _settings;
    private readonly TimeProvider _timeProvider;

    public MfaChallengeService(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<MfaSettings> settings,
        TimeProvider timeProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(ProtectionPurpose);
        _settings = settings.Value;
        _timeProvider = timeProvider;
    }

    public string Create(long userId, string securityStamp, bool rememberMe)
    {
        if (userId <= 0 || string.IsNullOrWhiteSpace(securityStamp))
            throw new ArgumentException("A valid user and security stamp are required.");

        var lifetime = GetLifetime();
        var issuedAt = _timeProvider.GetUtcNow().UtcDateTime;
        var challenge = new MfaChallengeData
        {
            UserId = userId,
            SecurityStamp = securityStamp,
            RememberMe = rememberMe,
            IssuedAtUtc = issuedAt,
            ExpiresAtUtc = issuedAt.Add(lifetime)
        };

        return _protector.Protect(JsonSerializer.Serialize(challenge));
    }

    public bool TryRead(string? token, out MfaChallengeData challenge)
    {
        challenge = new MfaChallengeData();
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
            return false;

        try
        {
            var candidate = JsonSerializer.Deserialize<MfaChallengeData>(
                _protector.Unprotect(token));
            if (candidate == null
                || candidate.UserId <= 0
                || string.IsNullOrWhiteSpace(candidate.SecurityStamp))
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var lifetime = GetLifetime();
            if (candidate.IssuedAtUtc > now.AddMinutes(1)
                || candidate.ExpiresAtUtc <= now
                || candidate.ExpiresAtUtc > candidate.IssuedAtUtc.Add(lifetime).AddSeconds(1))
            {
                return false;
            }

            challenge = candidate;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private TimeSpan GetLifetime()
    {
        if (_settings.ChallengeLifetimeMinutes is < 1 or > 15)
            throw new InvalidOperationException("MFA challenge lifetime must be between 1 and 15 minutes.");

        return TimeSpan.FromMinutes(_settings.ChallengeLifetimeMinutes);
    }
}
