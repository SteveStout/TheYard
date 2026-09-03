using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheBlock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bids",
                columns: table => new
                {
                    VehicleId = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<int>(type: "INTEGER", nullable: false),
                    BidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    WonBuyNow = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bids", x => x.VehicleId);
                });

            migrationBuilder.CreateTable(
                name: "Photos",
                columns: table => new
                {
                    File = table.Column<string>(type: "TEXT", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Style = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Photos", x => x.File);
                });

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Vin = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Make = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Trim = table.Column<string>(type: "TEXT", nullable: false),
                    BodyStyle = table.Column<string>(type: "TEXT", nullable: false),
                    ExteriorColor = table.Column<string>(type: "TEXT", nullable: false),
                    InteriorColor = table.Column<string>(type: "TEXT", nullable: false),
                    Engine = table.Column<string>(type: "TEXT", nullable: false),
                    Transmission = table.Column<string>(type: "TEXT", nullable: false),
                    Drivetrain = table.Column<string>(type: "TEXT", nullable: false),
                    OdometerKm = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", nullable: false),
                    ConditionGrade = table.Column<double>(type: "REAL", nullable: false),
                    ConditionReport = table.Column<string>(type: "TEXT", nullable: false),
                    DamageNotes = table.Column<string>(type: "TEXT", nullable: false),
                    TitleStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Province = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    AuctionStart = table.Column<string>(type: "TEXT", nullable: false),
                    StartingBid = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservePrice = table.Column<int>(type: "INTEGER", nullable: true),
                    BuyNowPrice = table.Column<int>(type: "INTEGER", nullable: true),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    SellingDealership = table.Column<string>(type: "TEXT", nullable: false),
                    Lot = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentBid = table.Column<int>(type: "INTEGER", nullable: true),
                    BidCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Seq",
                table: "Vehicles",
                column: "Seq",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bids");

            migrationBuilder.DropTable(
                name: "Photos");

            migrationBuilder.DropTable(
                name: "Vehicles");
        }
    }
}
