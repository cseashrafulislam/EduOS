using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

/// <summary>Additive permission for approved reservation end-date amendments.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261031170000_V2ReservationEndPeriodChanges")]
public sealed class V2ReservationEndPeriodChanges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances", "[Operation] IN (1,2,3,4)");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances",
            "([Operation] = 1 AND [DestinationBedId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3,4) AND [DestinationBedId] IS NULL AND [DestinationVersion] IS NULL)");

        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances", "[Operation] IN (1,2,3,4)");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances",
            "([Operation] = 1 AND [DestinationVehicleId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3,4) AND [DestinationVehicleId] IS NULL AND [DestinationVersion] IS NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [HostelFinanceClearances] WHERE [Operation] = 4) OR EXISTS (SELECT 1 FROM [TransportFinanceClearances] WHERE [Operation] = 4) THROW 51103, 'V2 reservation end-change history exists; a controlled reconciliation is required.', 1;");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances", "[Operation] IN (1,2,3)");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances",
            "([Operation] = 1 AND [DestinationBedId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3) AND [DestinationBedId] IS NULL AND [DestinationVersion] IS NULL)");

        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances", "[Operation] IN (1,2,3)");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances",
            "([Operation] = 1 AND [DestinationVehicleId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3) AND [DestinationVehicleId] IS NULL AND [DestinationVersion] IS NULL)");
    }
}
