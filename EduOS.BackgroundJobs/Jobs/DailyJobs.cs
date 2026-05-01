using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.BackgroundJobs.Jobs
{
    public class DailyJobs
    {
        [Hangfire.AutomaticRetry(Attempts = 3)]
        public async Task MarkAbsentStudents()
        {
            // Mark students as absent who weren't marked present
        }

        public async Task SendDailyAttendanceReport()
        {
            // Send report to parents
        }

        public async Task BackupDatabase()
        {
            // Take database backup
        }
    }
}
