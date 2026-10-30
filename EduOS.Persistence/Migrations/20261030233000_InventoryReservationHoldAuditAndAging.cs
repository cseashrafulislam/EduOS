using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

[DbContext(typeof(EduOSDbContext))]
[Migration("20261030233000_InventoryReservationHoldAuditAndAging")]
public sealed class InventoryReservationHoldAuditAndAging : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("ReservationHeldAtUtc", "InventoryMovements", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ReservationDeadlineScheduledAtUtc", "InventoryMovements", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>("ReservationDeadlineScheduledByUserId", "InventoryMovements", type: "bigint", nullable: true);
        migrationBuilder.AddCheckConstraint("CK_InventoryMovement_ScheduleAudit", "InventoryMovements",
            "([ReservationDeadlineScheduledAtUtc] IS NULL AND [ReservationDeadlineScheduledByUserId] IS NULL) OR ([ReservationDeadlineScheduledAtUtc] IS NOT NULL AND [ReservationDeadlineScheduledByUserId] IS NOT NULL AND [ReservationExpiresAtUtc] IS NOT NULL)");
        migrationBuilder.Sql(@"ALTER TRIGGER [dbo].[TR_InventoryMovement_ExpiryImmutable]
ON [dbo].[InventoryMovements] AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d
        LEFT JOIN inserted i ON i.Id = d.Id
        WHERE d.StateCode = N'Expired'
           OR (d.ReservationExpiresAtUtc IS NOT NULL
               AND (i.Id IS NULL OR i.ReservationExpiresAtUtc IS NULL
                    OR i.ReservationExpiresAtUtc <> d.ReservationExpiresAtUtc))
           OR (d.ReservationHeldAtUtc IS NOT NULL
               AND (i.Id IS NULL OR i.ReservationHeldAtUtc IS NULL
                    OR i.ReservationHeldAtUtc <> d.ReservationHeldAtUtc))
           OR (d.ReservationDeadlineScheduledAtUtc IS NOT NULL
               AND (i.Id IS NULL OR i.ReservationDeadlineScheduledAtUtc IS NULL
                    OR i.ReservationDeadlineScheduledAtUtc <> d.ReservationDeadlineScheduledAtUtc
                    OR i.ReservationDeadlineScheduledByUserId IS NULL
                    OR i.ReservationDeadlineScheduledByUserId <> d.ReservationDeadlineScheduledByUserId))
    )
        THROW 51061, 'Reservation hold and deadline evidence are immutable.', 1;
END");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM dbo.InventoryMovements
            WHERE ReservationHeldAtUtc IS NOT NULL OR ReservationDeadlineScheduledAtUtc IS NOT NULL
               OR ReservationDeadlineScheduledByUserId IS NOT NULL)
            THROW 51063, 'Reservation hold and scheduling evidence must be retained before rollback.', 1;");
        migrationBuilder.Sql(@"ALTER TRIGGER [dbo].[TR_InventoryMovement_ExpiryImmutable]
ON [dbo].[InventoryMovements] AFTER UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (
        SELECT 1 FROM deleted d
        LEFT JOIN inserted i ON i.Id = d.Id
        WHERE d.StateCode = N'Expired'
           OR (d.ReservationExpiresAtUtc IS NOT NULL
               AND (i.Id IS NULL OR i.ReservationExpiresAtUtc IS NULL
                    OR i.ReservationExpiresAtUtc <> d.ReservationExpiresAtUtc))
    )
        THROW 51061, 'Reservation deadline and expired evidence are immutable.', 1;
END");
        migrationBuilder.DropCheckConstraint("CK_InventoryMovement_ScheduleAudit", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationDeadlineScheduledByUserId", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationDeadlineScheduledAtUtc", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationHeldAtUtc", "InventoryMovements");
    }
}
