using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PulseOne.Infrastructure.Migrations.Hangfire
{
    /// <inheritdoc />
    public partial class InitialHangfire : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeadLetterJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    JobType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    Queue = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ExceptionType = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    ExceptionMessage = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    ExceptionDetail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    FailedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeadLetterJobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterJobs_FailedAt",
                table: "DeadLetterJobs",
                column: "FailedAt");

            migrationBuilder.CreateIndex(
                name: "IX_DeadLetterJobs_TenantId_FailedAt",
                table: "DeadLetterJobs",
                columns: new[] { "TenantId", "FailedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeadLetterJobs");
        }
    }
}
