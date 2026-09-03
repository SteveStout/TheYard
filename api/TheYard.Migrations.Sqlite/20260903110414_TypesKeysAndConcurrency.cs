using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class TypesKeysAndConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bids_UserId",
                table: "Bids");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Bids",
                type: "BLOB",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Photos_Seq",
                table: "Photos",
                column: "Seq",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Bids_AspNetUsers_UserId",
                table: "Bids",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bids_AspNetUsers_UserId",
                table: "Bids");

            migrationBuilder.DropIndex(
                name: "IX_Photos_Seq",
                table: "Photos");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Bids");

            migrationBuilder.CreateIndex(
                name: "IX_Bids_UserId",
                table: "Bids",
                column: "UserId");
        }
    }
}
