using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Text;

namespace EduOS.Persistence.Context
{
    public class EduOSDbContextFactory : IDesignTimeDbContextFactory<EduOSDbContext>
    {
        public EduOSDbContext CreateDbContext(string[] args)
        {
                var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            // 2. Set up the DbContextOptionsBuilder
            var builder = new DbContextOptionsBuilder<EduOSDbContext>();

            var connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? "Server=localhost;Database=EduOS;Trusted_Connection=True;MultipleActiveResultSets=true";

            builder.UseSqlServer(connectionString);
            return new EduOSDbContext(builder.Options);
        }
    }
}
