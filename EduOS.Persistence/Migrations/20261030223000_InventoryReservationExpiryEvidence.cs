using EduOS.Persistence.Context;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace EduOS.Persistence.Migrations;

/// <summary>Write-once reservation deadlines and immutable expiry evidence; no automatic expiry worker.</summary>
[DbContext(typeof(EduOSDbContext))]
[Migration("20261030223000_InventoryReservationExpiryEvidence")]
public sealed class InventoryReservationExpiryEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("ReservationExpiresAtUtc", "InventoryMovements", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<DateTime>("ReservationExpiredAtUtc", "InventoryMovements", type: "datetime2", nullable: true);
        migrationBuilder.AddColumn<long>("ReservationExpiredByUserId", "InventoryMovements", type: "bigint", nullable: true);
        migrationBuilder.AddColumn<string>("ReservationExpiryReason", "InventoryMovements", type: "nvarchar(400)", maxLength: 400, nullable: true);
        migrationBuilder.AddCheckConstraint("CK_InventoryMovement_ExpiryEvidence", "InventoryMovements",
            "([StateCode] <> N'Expired' AND [ReservationExpiredAtUtc] IS NULL AND [ReservationExpiredByUserId] IS NULL AND [ReservationExpiryReason] IS NULL) OR ([StateCode] = N'Expired' AND [ReservationExpiresAtUtc] IS NOT NULL AND [ReservationExpiredAtUtc] IS NOT NULL AND [ReservationExpiredByUserId] IS NOT NULL AND [ReservationExpiryReason] IS NOT NULL)");
        migrationBuilder.CreateIndex("IX_InventoryMovements_TenantId_StateCode_ReservationExpiresAtUtc",
            "InventoryMovements", new[] { "TenantId", "StateCode", "ReservationExpiresAtUtc" });
        migrationBuilder.Sql(@"CREATE TRIGGER [dbo].[TR_InventoryMovement_ExpiryImmutable]
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
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"IF EXISTS (SELECT 1 FROM dbo.InventoryMovements
            WHERE ReservationExpiresAtUtc IS NOT NULL OR ReservationExpiredAtUtc IS NOT NULL)
            THROW 51062, 'Reservation expiry evidence must be retained before rollback.', 1;");
        migrationBuilder.Sql("DROP TRIGGER [dbo].[TR_InventoryMovement_ExpiryImmutable];");
        migrationBuilder.DropIndex("IX_InventoryMovements_TenantId_StateCode_ReservationExpiresAtUtc", "InventoryMovements");
        migrationBuilder.DropCheckConstraint("CK_InventoryMovement_ExpiryEvidence", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationExpiryReason", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationExpiredByUserId", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationExpiredAtUtc", "InventoryMovements");
        migrationBuilder.DropColumn("ReservationExpiresAtUtc", "InventoryMovements");
    }
}
