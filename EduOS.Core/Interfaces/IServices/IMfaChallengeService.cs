using EduOS.Core.DTOs.Auth;

namespace EduOS.Core.Interfaces.IServices;

public interface IMfaChallengeService
{
    string Create(long userId, string securityStamp, bool rememberMe);
    bool TryRead(string? token, out MfaChallengeData challenge);
}
