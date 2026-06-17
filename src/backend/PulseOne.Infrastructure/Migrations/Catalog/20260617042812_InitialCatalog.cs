using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseOne.Infrastructure.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class InitialCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TenantShards",
                columns: table => new
                {
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ShardConnectionString = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: false),
                    Region = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Tier = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantShards", x => x.TenantId);
                });

            migrationBuilder.InsertData(
                table: "TenantShards",
                columns: new[] { "TenantId", "CreatedAt", "IsActive", "Region", "ShardConnectionString", "Tier" },
                values: new object[] { "demo", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), true, "westindia", "Server=(localdb)\\mssqllocaldb;Database=PulseOne_Shard01;Trusted_Connection=True;", 1 });

            migrationBuilder.CreateIndex(
                name: "IX_TenantShards_IsActive",
                table: "TenantShards",
                column: "IsActive");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantShards");
        }
    }
}
