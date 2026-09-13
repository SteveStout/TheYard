using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SiteActivity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityHours",
                columns: table => new
                {
                    Store = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Hour = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Requests = table.Column<int>(type: "INTEGER", nullable: false),
                    Bots = table.Column<int>(type: "INTEGER", nullable: false),
                    Paths = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityHours", x => new { x.Store, x.Hour });
                });

            migrationBuilder.CreateTable(
                name: "ActivityVisitors",
                columns: table => new
                {
                    Store = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    Day = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Visitor = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Network = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    FirstSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastSeen = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Requests = table.Column<int>(type: "INTEGER", nullable: false),
                    Bots = table.Column<int>(type: "INTEGER", nullable: false),
                    Paths = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityVisitors", x => new { x.Store, x.Day, x.Visitor });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityHours");

            migrationBuilder.DropTable(
                name: "ActivityVisitors");
        }
    }
}
