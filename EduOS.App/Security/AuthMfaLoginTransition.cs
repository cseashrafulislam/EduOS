using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;

namespace EduOS.App.Security;

public enum MfaLoginConsumption
{
    InvalidCode,
    Conflict,
    Completed
}

/// <summary>
/// Commits MFA recovery-code redemption, challenge stamp rotation and revocation
/// of previous cookie sessions in one SQL transaction. Never performs browser
/// cookie I/O or sends notifications inside the retryable transaction.
/// </summary>
public static class AuthMfaLoginTransition
{
    public static Task<MfaLoginConsumption> ConsumeAsync(
        EduOSDbContext db, UserManager<ApplicationUser> userManager,
        ApplicationUser user, string code, bool useRecoveryCode,
        CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync(async ct =>
        {
            var valid = useRecoveryCode
                ? (await userManager.RedeemTwoFactorRecoveryCodeAsync(user, code)).Succeeded
                : await userManager.VerifyTwoFactorTokenAsync(
                    user, TokenOptions.DefaultAuthenticatorProvider, code);
            if (!valid) return MfaLoginConsumption.InvalidCode;

            // Identity's optimistic concurrency stamp ensures a second request
            // holding the old signed challenge cannot complete independently.
            var stamp = await userManager.UpdateSecurityStampAsync(user);
            if (!stamp.Succeeded) return MfaLoginConsumption.Conflict;

            await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
            var reset = await userManager.ResetAccessFailedCountAsync(user);
            if (!reset.Succeeded)
                throw new InvalidOperationException("Unable to complete MFA sign-in.");
            return MfaLoginConsumption.Completed;
        }, cancellationToken);
}
