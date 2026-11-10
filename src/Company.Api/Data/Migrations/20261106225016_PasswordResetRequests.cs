using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Company.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PasswordResetRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResetRequestedUtc",
                table: "employee_credentials",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResetRequestedUtc",
                table: "employee_credentials");
        }
    }
}
