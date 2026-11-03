using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Company.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class TeamNudges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "team_nudges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmployeeCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_team_nudges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_team_nudges_employees_EmployeeCode",
                        column: x => x.EmployeeCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_team_nudges_employees_FromCode",
                        column: x => x.FromCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_team_nudges_EmployeeCode_Year_Month",
                table: "team_nudges",
                columns: new[] { "EmployeeCode", "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_team_nudges_FromCode",
                table: "team_nudges",
                column: "FromCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "team_nudges");
        }
    }
}
