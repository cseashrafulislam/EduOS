using EduOS.Core.Entities.SaaS;
using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace EduOS.Persistence.Seed
{
    public static class FeatureSeeder
    {
        public static async Task SeedAsync(EduOSDbContext context)
        {
            if (await context.Features.AnyAsync())
                return;

            var features = new List<Feature>
            {
                new Feature
                {
                    Name = "Student Management",
                    Code = "STUDENT_MANAGEMENT",
                    Description = "Manage students",
                    IsActive = true
                },
                new Feature
                {
                    Name = "Teacher Management",
                    Code = "TEACHER_MANAGEMENT",
                    Description = "Manage teachers",
                    IsActive = true
                },
                new Feature
                {
                    Name = "Attendance",
                    Code = "ATTENDANCE",
                    Description = "Attendance module",
                    IsActive = true
                },
                new Feature
                {
                    Name = "Fees",
                    Code = "FEES",
                    Description = "Fees and collections",
                    IsActive = true
                },
                new Feature
                {
                    Name = "Exam",
                    Code = "EXAM",
                    Description = "Exam and result module",
                    IsActive = true
                },
                new Feature
                {
                    Name = "Reports",
                    Code = "REPORTS",
                    Description = "Basic reporting",
                    IsActive = true
                }
            };

            await context.Features.AddRangeAsync(features);
            await context.SaveChangesAsync();
        }
    }
}