using EduOS.Core.Common;
using EduOS.Core.DTOs.Finance;
using EduOS.Core.Entities.Accounting;
using EduOS.Core.Entities.Finance;
using EduOS.Core.Enums.Domain;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
namespace EduOS.Service.Services.Finance;
/// <summary>Institutional fee GL; never handles Platform subscription invoices.</summary>
public sealed partial class StudentInvoicePostingService : IStudentInvoicePostingService
{
    private readonly EduOSDbContext db;
    private readonly ICurrentUserService user;
    private readonly TimeProvider clock;
    public StudentInvoicePostingService(EduOSDbContext db, ICurrentUserService user, TimeProvider clock)
    { this.db = db; this.user = user; this.clock = clock; }
    private bool CanRead => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal") || user.IsInRole("Accountant"));
    private bool CanPost => user.IsAuthenticated && user.TenantId > 0 && db.CurrentTenantId == user.TenantId &&
        (user.IsTenantAdmin || user.IsInRole("Principal"));
    private static ApiResponse<T> Error<T>(string message, int code = 400) =>
        ApiResponse<T>.ErrorResponse(message, code);
    private static bool Money(decimal value) => value >= 0m && value <= 9999999999999999.99m &&
        decimal.Round(value, 2) == value;
    private static bool Version(byte[] actual, string supplied)
    {
        try { return !string.IsNullOrWhiteSpace(supplied) &&
            actual.AsSpan().SequenceEqual(Convert.FromBase64String(supplied)); }
        catch (FormatException) { return false; }
    }
    private static Guid PostingKey(long tenantId, Guid invoice)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            "EduOS:Institution:StudentInvoice:" + tenantId + ":" + invoice.ToString("N")));
        return new Guid(bytes.AsSpan(0, 16));
    }
    private IQueryable<long> AllowedCampuses => db.UserCampusAccesses.AsNoTracking()
        .Where(x => x.TenantId == user.TenantId && x.UserId == user.UserId && x.IsActive && !x.IsDeleted)
        .Select(x => x.CampusId);
}
