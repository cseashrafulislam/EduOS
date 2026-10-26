using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EduOS.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReservationCommandReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "IdempotencyRecords",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CompletedByUserId",
                table: "IdempotencyRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResultJson",
                table: "IdempotencyRecords",
                type: "nvarchar(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TargetId",
                table: "IdempotencyRecords",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "CompletedByUserId",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "ResultJson",
                table: "IdempotencyRecords");

            migrationBuilder.DropColumn(
                name: "TargetId",
                table: "IdempotencyRecords");
        }
    }
}
