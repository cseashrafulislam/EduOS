using EduOS.Core.Entities.Auth;
using EduOS.Persistence.Context;
using Microsoft.AspNetCore.Identity;

namespace EduOS.App.Security;

/// <summary>
/// Coordinates credential changes with revocation of every registered cookie session.
/// Both Identity's password/security-stamp update and the token removals use the
/// request's canonical EduOSDbContext and the persistence execution strategy.
/// No external email, notification or other non-idempotent I/O occurs in the delegate.
/// </summary>
public static class AuthCredentialRotation
{
    public static Task<IdentityResult> ChangePasswordAsync(
        EduOSDbContext db, UserManager<ApplicationUser> userManager,
        ApplicationUser user, string currentPassword, string newPassword,
        CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync(async _ =>
        {
            // Preserve the existing profile-security modification timestamp.
            user.UpdatedAt = DateTime.UtcNow;
            var result = await userManager.ChangePasswordAsync(user, currentPassword, newPassword);
            if (!result.Succeeded) return result;

            await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
            return result;
        }, cancellationToken);

    public static Task<IdentityResult> ResetPasswordAsync(
        EduOSDbContext db, UserManager<ApplicationUser> userManager,
        ApplicationUser user, string resetToken, string newPassword,
        CancellationToken cancellationToken = default) =>
        db.ExecuteInTransactionAsync(async _ =>
        {
            var result = await userManager.ResetPasswordAsync(user, resetToken, newPassword);
            if (!result.Succeeded) return result;

            await AuthSessionRegistry.RevokeAllAsync(db, user.Id);
            return result;
        }, cancellationToken);
}
