using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aegis.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomersAndTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "customers");

            migrationBuilder.EnsureSchema(
                name: "transactions");

            migrationBuilder.CreateTable(
                name: "accounts",
                schema: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AccountType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                schema: "customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Country = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LegalName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                schema: "transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ValueDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    TransactionType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationAccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    DestinationCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    DestinationCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    CounterpartyCountry = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    Channel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProductCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CounterpartyId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RawSourceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    metadata = table.Column<Dictionary<string, string>>(type: "jsonb", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_transactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_tenant_id_customer_id",
                schema: "customers",
                table: "accounts",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_tenant_id_Id",
                schema: "customers",
                table: "accounts",
                columns: new[] { "tenant_id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_customers_tenant_id_Id",
                schema: "customers",
                table: "customers",
                columns: new[] { "tenant_id", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id_customer_id_Timestamp",
                schema: "transactions",
                table: "transactions",
                columns: new[] { "tenant_id", "customer_id", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_transactions_tenant_id_ExternalReference",
                schema: "transactions",
                table: "transactions",
                columns: new[] { "tenant_id", "ExternalReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "accounts",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "customers");

            migrationBuilder.DropTable(
                name: "transactions",
                schema: "transactions");
        }
    }
}
