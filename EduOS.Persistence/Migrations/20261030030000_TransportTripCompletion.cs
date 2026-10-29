using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Additive completion metadata and state checks. Down removes completion evidence:
/// only run on a nonproduction restored database with explicit history-retention approval.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030030000_TransportTripCompletion")]
public sealed class TransportTripCompletion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid?>("CompletionRequestId", "TransportTrips", type: "uniqueidentifier", nullable: true);
        migrationBuilder.AddColumn<DateTime?>("CompletedAt", "TransportTrips", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long?>("CompletedBy", "TransportTrips", type: "bigint", nullable: true);
        migrationBuilder.DropCheckConstraint("CK_TransportTrips_Dispatch", "TransportTrips");
        migrationBuilder.DropCheckConstraint("CK_TransportTrips_State", "TransportTrips");
        migrationBuilder.AddCheckConstraint("CK_TransportTrips_State", "TransportTrips", "[State] IN ('Planned','Dispatched','Completed')");
        migrationBuilder.AddCheckConstraint("CK_TransportTrips_Dispatch", "TransportTrips", "([State] = 'Planned' AND [TransportDriverId] IS NULL AND [DispatchRequestId] IS NULL AND [DispatchedAt] IS NULL AND [DispatchedBy] IS NULL AND [RiderCount] = 0 AND [CompletionRequestId] IS NULL AND [CompletedAt] IS NULL AND [CompletedBy] IS NULL) OR ([State] IN ('Dispatched','Completed') AND [TransportDriverId] IS NOT NULL AND [DispatchRequestId] IS NOT NULL AND [DispatchedAt] IS NOT NULL AND [DispatchedBy] IS NOT NULL AND [RiderCount] > 0 AND (([State] = 'Dispatched' AND [CompletionRequestId] IS NULL AND [CompletedAt] IS NULL AND [CompletedBy] IS NULL) OR ([State] = 'Completed' AND [CompletionRequestId] IS NOT NULL AND [CompletedAt] IS NOT NULL AND [CompletedBy] IS NOT NULL)))");
        migrationBuilder.CreateIndex("IX_TransportTrips_TenantId_CompletionRequestId",
            "TransportTrips", new[] { "TenantId", "CompletionRequestId" },
            unique: true, filter: "[CompletionRequestId] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_TransportTrips_TenantId_CompletionRequestId", "TransportTrips");
        migrationBuilder.DropCheckConstraint("CK_TransportTrips_Dispatch", "TransportTrips");
        migrationBuilder.DropCheckConstraint("CK_TransportTrips_State", "TransportTrips");
        migrationBuilder.AddCheckConstraint("CK_TransportTrips_State", "TransportTrips", "[State] IN ('Planned','Dispatched')");
        migrationBuilder.AddCheckConstraint("CK_TransportTrips_Dispatch", "TransportTrips",
            "([State] = 'Planned' AND [TransportDriverId] IS NULL AND [DispatchRequestId] IS NULL AND [DispatchedAt] IS NULL AND [DispatchedBy] IS NULL AND [RiderCount] = 0) OR ([State] = 'Dispatched' AND [TransportDriverId] IS NOT NULL AND [DispatchRequestId] IS NOT NULL AND [DispatchedAt] IS NOT NULL AND [DispatchedBy] IS NOT NULL AND [RiderCount] > 0)");
        migrationBuilder.DropColumn("CompletionRequestId", "TransportTrips");
        migrationBuilder.DropColumn("CompletedAt", "TransportTrips");
        migrationBuilder.DropColumn("CompletedBy", "TransportTrips");
    }
}
