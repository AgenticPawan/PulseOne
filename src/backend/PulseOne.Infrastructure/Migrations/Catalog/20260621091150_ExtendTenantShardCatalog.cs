using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseOne.Infrastructure.Migrations.Catalog
{
    /// <inheritdoc />
    public partial class ExtendTenantShardCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AdminEmail",
                table: "TenantShards",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "TenantShards",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ShardLabel",
                table: "TenantShards",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "TenantShards",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.UpdateData(
                table: "TenantShards",
                keyColumn: "TenantId",
                keyValue: "demo",
                columns: new[] { "AdminEmail", "Name", "ShardLabel", "Status" },
                values: new object[] { "admin@demo.pulseone.local", "Demo Tenant", "Shard01", 1 });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminEmail",
                table: "TenantShards");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "TenantShards");

            migrationBuilder.DropColumn(
                name: "ShardLabel",
                table: "TenantShards");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "TenantShards");
        }
    }
}
