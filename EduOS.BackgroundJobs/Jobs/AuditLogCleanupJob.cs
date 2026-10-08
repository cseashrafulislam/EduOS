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
            const int tenantPageSize = 100;
            const int deleteBatchSize = 500;
            var cutoffDate = DateTime.UtcNow.AddYears(-2);
            long lastTenantId = 0;
            long totalDeleted = 0;

            // A background job has no request tenant. Establish an explicit trusted
            // tenant scope for each isolated batch; never bypass audit query filters.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tenantIds = await _context.Tenants.AsNoTracking()
                    .Where(t => t.Id > lastTenantId && !t.IsDeleted)
                    .OrderBy(t => t.Id)
                    .Select(t => t.Id)
                    .Take(tenantPageSize)
                    .ToListAsync(cancellationToken);
                if (tenantIds.Count == 0) break;

                foreach (var tenantId in tenantIds)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using (_context.BeginSystemTenantScope(tenantId))
                    {
                        while (true)
                        {
                            var ids = await _context.AuditLogs.AsNoTracking()
                                .Where(a => a.TenantId == tenantId && a.OccurredAt < cutoffDate)
                                .OrderBy(a => a.Id)
                                .Select(a => a.Id)
                                .Take(deleteBatchSize)
                                .ToArrayAsync(cancellationToken);
                            if (ids.Length == 0) break;

                            var deleted = await _context.AuditLogs
                                .Where(a => a.TenantId == tenantId && ids.Contains(a.Id) && a.OccurredAt < cutoffDate)
                                .ExecuteDeleteAsync(cancellationToken);
                            totalDeleted += deleted;
                            if (deleted == 0) break;
                        }
                    }
                }

                lastTenantId = tenantIds[^1];
            }

            // Platform-level (TenantId == null) audit records are deliberately retained.
            _logger.LogInformation("Cleaned up {Count} expired tenant audit logs", totalDeleted);
        }
    }
}
