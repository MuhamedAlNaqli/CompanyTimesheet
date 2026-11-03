using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Company.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SupervisorCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsFinalApprover = table.Column<bool>(type: "boolean", nullable: false),
                    DesignatedApproverCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IsFinance = table.Column<bool>(type: "boolean", nullable: false),
                    ActiveFrom = table.Column<DateOnly>(type: "date", nullable: true),
                    ActiveTo = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employees", x => x.Id);
                    table.UniqueConstraint("AK_employees_Code", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "month_sheets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Chain = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_month_sheets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "religious_holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_religious_holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "state_holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_state_holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "employee_accounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Account = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_employee_accounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_accounts_employees_EmployeeCode",
                        column: x => x.EmployeeCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sheet_entries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Code = table.Column<int>(type: "integer", nullable: false),
                    Minutes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_entries_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sheet_events",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    AtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ActorCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    OnBehalfOf = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Step = table.Column<int>(type: "integer", nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_events_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sheet_messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    FromCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    Answer = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_messages_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sheet_overtime",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    End = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_overtime", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_overtime_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sheet_revisions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SheetId = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<int>(type: "integer", nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Json = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sheet_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sheet_revisions_month_sheets_SheetId",
                        column: x => x.SheetId,
                        principalTable: "month_sheets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_AtUtc",
                table: "audit_events",
                column: "AtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_employee_accounts_Account",
                table: "employee_accounts",
                column: "Account",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_employee_accounts_EmployeeCode",
                table: "employee_accounts",
                column: "EmployeeCode");

            migrationBuilder.CreateIndex(
                name: "IX_employees_Code",
                table: "employees",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_month_sheets_EmployeeCode_Year_Month",
                table: "month_sheets",
                columns: new[] { "EmployeeCode", "Year", "Month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_religious_holidays_EmployeeCode_Date",
                table: "religious_holidays",
                columns: new[] { "EmployeeCode", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sheet_entries_SheetId_Date_Code",
                table: "sheet_entries",
                columns: new[] { "SheetId", "Date", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sheet_events_SheetId",
                table: "sheet_events",
                column: "SheetId");

            migrationBuilder.CreateIndex(
                name: "IX_sheet_messages_SheetId_Position",
                table: "sheet_messages",
                columns: new[] { "SheetId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sheet_overtime_SheetId_Date",
                table: "sheet_overtime",
                columns: new[] { "SheetId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_sheet_revisions_SheetId_Revision",
                table: "sheet_revisions",
                columns: new[] { "SheetId", "Revision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_state_holidays_Date",
                table: "state_holidays",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");

            migrationBuilder.DropTable(
                name: "employee_accounts");

            migrationBuilder.DropTable(
                name: "religious_holidays");

            migrationBuilder.DropTable(
                name: "sheet_entries");

            migrationBuilder.DropTable(
                name: "sheet_events");

            migrationBuilder.DropTable(
                name: "sheet_messages");

            migrationBuilder.DropTable(
                name: "sheet_overtime");

            migrationBuilder.DropTable(
                name: "sheet_revisions");

            migrationBuilder.DropTable(
                name: "state_holidays");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "month_sheets");
        }
    }
}
