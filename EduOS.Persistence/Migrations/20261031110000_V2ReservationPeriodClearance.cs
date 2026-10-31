using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261031110000_V2ReservationPeriodClearance")]
public partial class V2ReservationPeriodClearance : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>("ApprovedNetAdjustment", "HostelFinanceClearances",
            type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("SuggestedNetAdjustment", "HostelFinanceClearances",
            type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("ApprovedNetAdjustment", "TransportFinanceClearances",
            type: "decimal(18,2)", nullable: false, defaultValue: 0m);
        migrationBuilder.AddColumn<decimal>("SuggestedNetAdjustment", "TransportFinanceClearances",
            type: "decimal(18,2)", nullable: false, defaultValue: 0m);

        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances",
            "[Operation] IN (1,2,3)");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances",
            "([Operation] = 1 AND [DestinationBedId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3) AND [DestinationBedId] IS NULL AND [DestinationVersion] IS NULL)");

        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances",
            "[Operation] IN (1,2,3)");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances",
            "([Operation] = 1 AND [DestinationVehicleId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] IN (2,3) AND [DestinationVehicleId] IS NULL AND [DestinationVersion] IS NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Revert to the old domain constraints only if no new operation was ever created.
        migrationBuilder.Sql("IF EXISTS(SELECT 1 FROM [HostelFinanceClearances] WHERE [Operation] = 3) OR EXISTS(SELECT 1 FROM [TransportFinanceClearances] WHERE [Operation] = 3) THROW 51102, 'V2 period clearance history exists; reverting needs controlled reconciliation.', 1;");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Operation", "HostelFinanceClearances", "[Operation] IN (1,2)");
        migrationBuilder.AddCheckConstraint("CK_HostelFinanceClearance_Target", "HostelFinanceClearances",
            "([Operation] = 1 AND [DestinationBedId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] = 2 AND [DestinationBedId] IS NULL AND [DestinationVersion] IS NULL)");
        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances");
        migrationBuilder.DropCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Operation", "TransportFinanceClearances", "[Operation] IN (1,2)");
        migrationBuilder.AddCheckConstraint("CK_TransportFinanceClearance_Target", "TransportFinanceClearances",
            "([Operation] = 1 AND [DestinationVehicleId] IS NOT NULL AND [DestinationVersion] IS NOT NULL) OR ([Operation] = 2 AND [DestinationVehicleId] IS NULL AND [DestinationVersion] IS NULL)");

        migrationBuilder.DropColumn("ApprovedNetAdjustment", "HostelFinanceClearances");
        migrationBuilder.DropColumn("SuggestedNetAdjustment", "HostelFinanceClearances");
        migrationBuilder.DropColumn("ApprovedNetAdjustment", "TransportFinanceClearances");
        migrationBuilder.DropColumn("SuggestedNetAdjustment", "TransportFinanceClearances");
    }
}
