using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;

namespace EduOS.App.Security;

public enum MfaLoginConsumption
{
    InvalidCode,
    Completed
}

/// <summary>
/// Commits MFA recovery-code redemption, challenge stamp rotation and revocation
/// of previous cookie sessions in one SQL transaction. Never performs browser
/// cookie I/O or sends notifications inside the retryable transaction.
/// </summary>
public sealed class MfaLoginConflictException : Exception;

public static class AuthMfaLoginTransition
{
    public static Task<MfaLoginConsumption> ConsumeAsync(
        EduOSDbContext db, UserManager<ApplicationUser> userManager,
        ApplicationUser user, string code, bool useRecoveryCode,
        CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync(async ct =>
        {
            if (useRecoveryCode)
            {
                var redeemed = await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);
                if (!redeemed.Succeeded)
                {
                    // An optimistic-concurrency failure after token-store work must
                    // roll back, not commit a consumed code as an invalid attempt.
                    var concurrencyCode = new IdentityErrorDescriber().ConcurrencyFailure().Code;
                    if (redeemed.Errors.Any(x => x.Code == concurrencyCode))
                        throw new MfaLoginConflictException();
                    return MfaLoginConsumption.InvalidCode;
                }
            }
            else if (!await userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, code))
                return MfaLoginConsumption.InvalidCode;

            // Identity's optimistic concurrency stamp ensures a second request
            // holding the old signed challenge cannot complete independently.
            var stamp = await userManager.UpdateSecurityStampAsync(user);
            if (!stamp.Succeeded) throw new MfaLoginConflictException();

            await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
            var reset = await userManager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded)
                throw new InvalidOperationException("Unable to complete MFA sign-in.");
            return MfaLoginConsumption.Completed;
        }, cancellationToken);
}
