using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs
{
    public class AuditLogCleanupJob
    {
        private readonly EduOSDbContext _context;
        private readonly ILogger<AuditLogCleanupJob> _logger;

        public async Task CleanupAsync()
        {
            var cutoffDate = DateTime.UtcNow.AddYears(-2); // Keep 2 years

            var oldLogs = await _context.AuditLogs
                .Where(a => a.CreatedAt < cutoffDate)
                .ToListAsync();

            _context.AuditLogs.RemoveRange(oldLogs);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Cleaned up {Count} old audit logs", oldLogs.Count);
        }
    }

}
