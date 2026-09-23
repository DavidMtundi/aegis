using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlertsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "alerts");

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "alerts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    FocusType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FocusEntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Severity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RiskScore = table.Column<int>(type: "integer", nullable: false),
                    TriggeredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    evidence_json = table.Column<string>(type: "jsonb", nullable: false),
                    deduplication_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    AssignedTo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alerts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alerts_tenant_id_deduplication_key",
                schema: "alerts",
                table: "alerts",
                columns: new[] { "tenant_id", "deduplication_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alerts_tenant_id_Status",
                schema: "alerts",
                table: "alerts",
                columns: new[] { "tenant_id", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "alerts");
        }
    }
}
