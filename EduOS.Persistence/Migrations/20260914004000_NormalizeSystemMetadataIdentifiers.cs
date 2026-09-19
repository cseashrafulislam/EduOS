using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    public partial class NormalizeSystemMetadataIdentifiers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF EXISTS (
    SELECT 1 FROM [AlbumPhotos]
    WHERE [AlbumId1] IS NOT NULL AND [AlbumId] > 0 AND CONVERT(bigint, [AlbumId]) <> [AlbumId1])
    THROW 51000, 'AlbumPhotos contains conflicting AlbumId and AlbumId1 values.', 1;

IF EXISTS (
    SELECT 1 FROM [CustomFieldValues]
    WHERE [CustomFieldId1] IS NOT NULL AND [CustomFieldId] > 0 AND CONVERT(bigint, [CustomFieldId]) <> [CustomFieldId1])
    THROW 51000, 'CustomFieldValues contains conflicting CustomFieldId and CustomFieldId1 values.', 1;

IF EXISTS (
    SELECT 1 FROM [AlbumPhotos] p
    WHERE COALESCE(NULLIF(CONVERT(bigint, p.[AlbumId]), 0), p.[AlbumId1]) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM [Albums] a
           WHERE a.[Id] = COALESCE(NULLIF(CONVERT(bigint, p.[AlbumId]), 0), p.[AlbumId1])))
    THROW 51000, 'AlbumPhotos contains a missing or orphan album reference.', 1;

IF EXISTS (
    SELECT 1 FROM [CustomFieldValues] v
    WHERE COALESCE(NULLIF(CONVERT(bigint, v.[CustomFieldId]), 0), v.[CustomFieldId1]) IS NULL
       OR NOT EXISTS (
           SELECT 1 FROM [CustomFields] f
           WHERE f.[Id] = COALESCE(NULLIF(CONVERT(bigint, v.[CustomFieldId]), 0), v.[CustomFieldId1])))
    THROW 51000, 'CustomFieldValues contains a missing or orphan custom-field reference.', 1;

IF OBJECT_ID(N'[FK_AlbumPhotos_Albums_AlbumId1]', 'F') IS NOT NULL
    ALTER TABLE [AlbumPhotos] DROP CONSTRAINT [FK_AlbumPhotos_Albums_AlbumId1];
IF OBJECT_ID(N'[FK_CustomFieldValues_CustomFields_CustomFieldId1]', 'F') IS NOT NULL
    ALTER TABLE [CustomFieldValues] DROP CONSTRAINT [FK_CustomFieldValues_CustomFields_CustomFieldId1];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AlbumPhotos_AlbumId1' AND object_id = OBJECT_ID(N'[AlbumPhotos]'))
    DROP INDEX [IX_AlbumPhotos_AlbumId1] ON [AlbumPhotos];
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_CustomFieldValues_CustomFieldId1' AND object_id = OBJECT_ID(N'[CustomFieldValues]'))
    DROP INDEX [IX_CustomFieldValues_CustomFieldId1] ON [CustomFieldValues];
");

            migrationBuilder.AlterColumn<long>(
                name: "AlbumId",
                table: "AlbumPhotos",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "CustomFieldId",
                table: "CustomFieldValues",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<long>(
                name: "EntityId",
                table: "CustomFieldValues",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.Sql(@"
UPDATE [AlbumPhotos]
SET [AlbumId] = [AlbumId1]
WHERE [AlbumId] <= 0 AND [AlbumId1] IS NOT NULL;

UPDATE [CustomFieldValues]
SET [CustomFieldId] = [CustomFieldId1]
WHERE [CustomFieldId] <= 0 AND [CustomFieldId1] IS NOT NULL;
");

            migrationBuilder.DropColumn(name: "AlbumId1", table: "AlbumPhotos");
            migrationBuilder.DropColumn(name: "CustomFieldId1", table: "CustomFieldValues");

            migrationBuilder.CreateIndex(
                name: "IX_AlbumPhotos_AlbumId",
                table: "AlbumPhotos",
                column: "AlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomFieldValues_CustomFieldId",
                table: "CustomFieldValues",
                column: "CustomFieldId");

            migrationBuilder.AddForeignKey(
                name: "FK_AlbumPhotos_Albums_AlbumId",
                table: "AlbumPhotos",
                column: "AlbumId",
                principalTable: "Albums",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomFieldValues_CustomFields_CustomFieldId",
                table: "CustomFieldValues",
                column: "CustomFieldId",
                principalTable: "CustomFields",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "System metadata identifiers cannot be safely narrowed from bigint to int after normalization.");
        }
    }
}
