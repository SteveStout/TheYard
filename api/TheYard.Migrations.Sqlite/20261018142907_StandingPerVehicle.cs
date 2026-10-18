using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class StandingPerVehicle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Standings",
                columns: table => new
                {
                    VehicleId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    BidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HighBidderId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SoldBuyNow = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Standings", x => x.VehicleId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Standings");
        }
    }
}
