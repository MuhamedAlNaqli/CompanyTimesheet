using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Company.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Deputies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "approver_deputies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ApproverCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DeputyCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_approver_deputies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_approver_deputies_employees_ApproverCode",
                        column: x => x.ApproverCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_approver_deputies_employees_DeputyCode",
                        column: x => x.DeputyCode,
                        principalTable: "employees",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_approver_deputies_ApproverCode_FromDate",
                table: "approver_deputies",
                columns: new[] { "ApproverCode", "FromDate" });

            migrationBuilder.CreateIndex(
                name: "IX_approver_deputies_DeputyCode_FromDate_ToDate",
                table: "approver_deputies",
                columns: new[] { "DeputyCode", "FromDate", "ToDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approver_deputies");
        }
    }
}
