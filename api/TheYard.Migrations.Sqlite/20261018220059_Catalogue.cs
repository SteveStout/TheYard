using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Catalogue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Catalogue",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Seq = table.Column<int>(type: "INTEGER", nullable: false),
                    Vin = table.Column<string>(type: "TEXT", unicode: false, maxLength: 17, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Make = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Trim = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BodyStyle = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExteriorColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    InteriorColor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Engine = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Transmission = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Drivetrain = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    OdometerKm = table.Column<int>(type: "INTEGER", nullable: false),
                    FuelType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ConditionGrade = table.Column<double>(type: "REAL", nullable: false),
                    ConditionReport = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    DamageNotes = table.Column<string>(type: "TEXT", nullable: false),
                    TitleStatus = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    AuctionStart = table.Column<string>(type: "TEXT", nullable: false),
                    StartingBid = table.Column<int>(type: "INTEGER", nullable: false),
                    ReservePrice = table.Column<int>(type: "INTEGER", nullable: true),
                    BuyNowPrice = table.Column<int>(type: "INTEGER", nullable: true),
                    Images = table.Column<string>(type: "TEXT", nullable: false),
                    SellingDealership = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Lot = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CurrentBid = table.Column<int>(type: "INTEGER", nullable: true),
                    BidCount = table.Column<int>(type: "INTEGER", nullable: false),
                    StartOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    EndOffsetMs = table.Column<long>(type: "INTEGER", nullable: false),
                    Price = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchText = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Catalogue", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_BidCount_Seq_StartOffsetMs_EndOffsetMs",
                table: "Catalogue",
                columns: new[] { "BidCount", "Seq", "StartOffsetMs", "EndOffsetMs" },
                descending: new[] { true, false, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_ConditionGrade_Seq_StartOffsetMs_EndOffsetMs",
                table: "Catalogue",
                columns: new[] { "ConditionGrade", "Seq", "StartOffsetMs", "EndOffsetMs" },
                descending: new[] { true, false, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_Counts",
                table: "Catalogue",
                columns: new[] { "Province", "TitleStatus", "BodyStyle", "Make", "ConditionGrade", "Price", "StartOffsetMs", "EndOffsetMs" });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_Words",
                table: "Catalogue",
                columns: new[] { "SearchText", "StartOffsetMs", "EndOffsetMs", "Price", "ConditionGrade", "BidCount", "Seq" });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_EndOffsetMs_Seq_StartOffsetMs",
                table: "Catalogue",
                columns: new[] { "EndOffsetMs", "Seq", "StartOffsetMs" });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_Price_Seq_StartOffsetMs_EndOffsetMs",
                table: "Catalogue",
                columns: new[] { "Price", "Seq", "StartOffsetMs", "EndOffsetMs" });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_PriceDescending",
                table: "Catalogue",
                columns: new[] { "Price", "Seq", "StartOffsetMs", "EndOffsetMs" },
                descending: new[] { true, false, false, false });

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_Seq",
                table: "Catalogue",
                column: "Seq",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Catalogue_StartOffsetMs_Seq_EndOffsetMs",
                table: "Catalogue",
                columns: new[] { "StartOffsetMs", "Seq", "EndOffsetMs" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Catalogue");
        }
    }
}
