using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Company.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "sheet_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "CurrentApproverCode",
                table: "month_sheets",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Hash",
                table: "audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousHash",
                table: "audit_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sheet_chain_steps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    EmployeeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_chain_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_chain_steps_employees_EmployeeCode",
                        column: x => x.EmployeeCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sheet_chain_steps_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_month_sheets_CurrentApproverCode_Status",
                table: "month_sheets",
                columns: new[] { "CurrentApproverCode", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_PreviousHash",
                table: "audit_events",
                column: "PreviousHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sheet_chain_steps_EmployeeCode",
                table: "sheet_chain_steps",
                column: "EmployeeCode");

            migrationBuilder.CreateIndex(
                name: "IX_sheet_chain_steps_SheetId_Position",
                table: "sheet_chain_steps",
                columns: new[] { "SheetId", "Position" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_month_sheets_employees_EmployeeCode",
                table: "month_sheets",
                column: "EmployeeCode",
                principalTable: "employees",
                principalColumn: "Code",
                onDelete: ReferentialAction.Restrict);

            // The chain of sheets that were already submitted moves from the comma separated text to the table, in the same order
            migrationBuilder.Sql(
                "INSERT INTO sheet_chain_steps (\"SheetId\", \"Position\", \"EmployeeCode\") " +
                "SELECT s.\"Id\", t.ord - 1, trim(t.code) FROM month_sheets s " +
                "CROSS JOIN LATERAL unnest(string_to_array(s.\"Chain\", ',')) WITH ORDINALITY AS t(code, ord) " +
                "WHERE s.\"Chain\" <> ''");

            // Whose turn it is, for the sheets that are waiting
            migrationBuilder.Sql(
                "UPDATE month_sheets m SET \"CurrentApproverCode\" = " +
                "(SELECT c.\"EmployeeCode\" FROM sheet_chain_steps c WHERE c.\"SheetId\" = m.\"Id\" AND c.\"Position\" = m.\"Step\") " +
                "WHERE m.\"Status\" = 'Submitted'");

            migrationBuilder.DropColumn(
                name: "Chain",
                table: "month_sheets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_month_sheets_employees_EmployeeCode",
                table: "month_sheets");

            migrationBuilder.DropTable(
                name: "sheet_chain_steps");

            migrationBuilder.DropIndex(
                name: "IX_month_sheets_CurrentApproverCode_Status",
                table: "month_sheets");

            migrationBuilder.DropIndex(
                name: "IX_audit_events_PreviousHash",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "sheet_revisions");

            migrationBuilder.DropColumn(
                name: "CurrentApproverCode",
                table: "month_sheets");

            migrationBuilder.DropColumn(
                name: "Hash",
                table: "audit_events");

            migrationBuilder.DropColumn(
                name: "PreviousHash",
                table: "audit_events");

            migrationBuilder.AddColumn<string>(
                name: "Chain",
                table: "month_sheets",
                type: "character varying(400)",
                maxLength: 400,
                nullable: false,
                defaultValue: "");
        }
    }
}
