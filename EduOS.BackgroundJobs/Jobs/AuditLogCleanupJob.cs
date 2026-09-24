using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace EduOS.BackgroundJobs.Jobs
{
    public class AuditLogCleanupJob
    {
        private readonly EduOSDbContext _context;
        private readonly ILogger<AuditLogCleanupJob> _logger;

        public AuditLogCleanupJob(
            EduOSDbContext context,
            ILogger<AuditLogCleanupJob> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task CleanupAsync(CancellationToken cancellationToken = default)
        {
            var cutoffDate = DateTime.UtcNow.AddYears(-2);

            var deleted = await _context.AuditLogs
                .IgnoreQueryFilters()
                .Where(a => a.CreatedAt < cutoffDate)
                .ExecuteDeleteAsync(cancellationToken);

            _logger.LogInformation("Cleaned up {Count} old audit logs", deleted);
        }
    }
}
