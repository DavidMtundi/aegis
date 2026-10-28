using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScreening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "screening");

            migrationBuilder.AddColumn<DateOnly>(
                name: "DateOfBirth",
                schema: "customers",
                table: "customers",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "screening_matches",
                schema: "screening",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    MatchedName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false),
                    BirthYearMatched = table.Column<bool>(type: "boolean", nullable: false),
                    subject_json = table.Column<string>(type: "jsonb", nullable: false),
                    Trigger = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_screening_matches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_watchlist_entries",
                schema: "screening",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    names_json = table.Column<string>(type: "jsonb", nullable: false),
                    birth_years_json = table.Column<string>(type: "jsonb", nullable: false),
                    countries_json = table.Column<string>(type: "jsonb", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_watchlist_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_entries",
                schema: "screening",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    names_json = table.Column<string>(type: "jsonb", nullable: false),
                    birth_years_json = table.Column<string>(type: "jsonb", nullable: false),
                    countries_json = table.Column<string>(type: "jsonb", nullable: false),
                    Detail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    load_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_loads",
                schema: "screening",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    LoadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EntryCount = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_watchlist_loads", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_screening_matches_tenant_id_customer_id_Source_ExternalId",
                schema: "screening",
                table: "screening_matches",
                columns: new[] { "tenant_id", "customer_id", "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_screening_matches_tenant_id_Status_CreatedAt",
                schema: "screening",
                table: "screening_matches",
                columns: new[] { "tenant_id", "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_tenant_watchlist_entries_tenant_id_ExternalId",
                schema: "screening",
                table: "tenant_watchlist_entries",
                columns: new[] { "tenant_id", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_entries_Source_ExternalId",
                schema: "screening",
                table: "watchlist_entries",
                columns: new[] { "Source", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_watchlist_loads_Source_LoadedAt",
                schema: "screening",
                table: "watchlist_loads",
                columns: new[] { "Source", "LoadedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "screening_matches",
                schema: "screening");

            migrationBuilder.DropTable(
                name: "tenant_watchlist_entries",
                schema: "screening");

            migrationBuilder.DropTable(
                name: "watchlist_entries",
                schema: "screening");

            migrationBuilder.DropTable(
                name: "watchlist_loads",
                schema: "screening");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                schema: "customers",
                table: "customers");
        }
    }
}
