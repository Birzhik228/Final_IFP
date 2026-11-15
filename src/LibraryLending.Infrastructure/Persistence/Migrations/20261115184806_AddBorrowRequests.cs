using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LibraryLending.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BorrowRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookId = table.Column<int>(type: "INTEGER", nullable: false),
                    MemberId = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ReviewerUserId = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewNote = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    LoanId = table.Column<int>(type: "INTEGER", nullable: true),
                    Version = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorrowRequests", x => x.Id);
                    table.CheckConstraint("CK_BorrowRequest_Review", "length(\"ReviewNote\") <= 500 AND ((\"Status\" = 'Pending' AND \"ReviewedAtUtc\" IS NULL AND \"ReviewerUserId\" IS NULL AND \"LoanId\" IS NULL) OR (\"Status\" = 'Accepted' AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewerUserId\" IS NOT NULL AND \"LoanId\" IS NOT NULL) OR (\"Status\" = 'Rejected' AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewerUserId\" IS NOT NULL AND \"LoanId\" IS NULL) OR (\"Status\" = 'Cancelled' AND \"ReviewedAtUtc\" IS NOT NULL AND \"LoanId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_BorrowRequests_AspNetUsers_ReviewerUserId",
                        column: x => x.ReviewerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BorrowRequests_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BorrowRequests_Loans_LoanId",
                        column: x => x.LoanId,
                        principalTable: "Loans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BorrowRequests_Members_MemberId",
                        column: x => x.MemberId,
                        principalTable: "Members",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_BookId_MemberId",
                table: "BorrowRequests",
                columns: new[] { "BookId", "MemberId" },
                unique: true,
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_LoanId",
                table: "BorrowRequests",
                column: "LoanId",
                unique: true,
                filter: "\"LoanId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_MemberId_CreatedAtUtc",
                table: "BorrowRequests",
                columns: new[] { "MemberId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_ReviewerUserId",
                table: "BorrowRequests",
                column: "ReviewerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_Status_CreatedAtUtc_Id",
                table: "BorrowRequests",
                columns: new[] { "Status", "CreatedAtUtc", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BorrowRequests");
        }
    }
}
