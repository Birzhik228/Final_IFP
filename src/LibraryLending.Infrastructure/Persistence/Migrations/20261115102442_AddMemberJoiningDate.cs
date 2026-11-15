using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryLending.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMemberJoiningDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "JoinedAtUtc",
                table: "Members",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "JoinedAtUtc",
                table: "Members");
        }
    }
}
