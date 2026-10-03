using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskScoring : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "customer_risk_scores",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelVersion = table.Column<int>(type: "integer", nullable: false),
                    Score = table.Column<int>(type: "integer", nullable: false),
                    Band = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    contributions_json = table.Column<string>(type: "jsonb", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CalculatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_risk_scores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "risk_models",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    factors_json = table.Column<string>(type: "jsonb", nullable: false),
                    band_medium = table.Column<int>(type: "integer", nullable: false),
                    band_high = table.Column<int>(type: "integer", nullable: false),
                    band_critical = table.Column<int>(type: "integer", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_models", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_risk_scores_tenant_id_customer_id_CalculatedAt",
                schema: "risk",
                table: "customer_risk_scores",
                columns: new[] { "tenant_id", "customer_id", "CalculatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_risk_models_tenant_id_Version",
                schema: "risk",
                table: "risk_models",
                columns: new[] { "tenant_id", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "customer_risk_scores",
                schema: "risk");

            migrationBuilder.DropTable(
                name: "risk_models",
                schema: "risk");
        }
    }
}
