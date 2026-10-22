using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Blog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Post",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Slug = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LinkedInUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    CodeUrl = table.Column<string>(type: "TEXT", maxLength: 400, nullable: true),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Post", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PostEvent",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    SessionHash = table.Column<byte[]>(type: "BLOB", fixedLength: true, maxLength: 32, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", unicode: false, maxLength: 16, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsBot = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostEvent_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PostImage",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostImage", x => new { x.PostId, x.Position });
                    table.ForeignKey(
                        name: "FK_PostImage_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostTag",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostTag", x => new { x.PostId, x.Tag });
                    table.ForeignKey(
                        name: "FK_PostTag_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostView",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    ViewedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", unicode: false, maxLength: 16, nullable: false),
                    RefCode = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 6, nullable: true),
                    ReferrerHost = table.Column<string>(type: "TEXT", unicode: false, maxLength: 120, nullable: true),
                    UtmSource = table.Column<string>(type: "TEXT", unicode: false, maxLength: 60, nullable: true),
                    UtmMedium = table.Column<string>(type: "TEXT", unicode: false, maxLength: 60, nullable: true),
                    UtmCampaign = table.Column<string>(type: "TEXT", unicode: false, maxLength: 60, nullable: true),
                    Device = table.Column<string>(type: "TEXT", unicode: false, maxLength: 8, nullable: false),
                    Country = table.Column<string>(type: "TEXT", unicode: false, fixedLength: true, maxLength: 2, nullable: true),
                    SessionHash = table.Column<byte[]>(type: "BLOB", fixedLength: true, maxLength: 32, nullable: false),
                    IsBot = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostView", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PostView_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Post_LinkedInUrl",
                table: "Post",
                column: "LinkedInUrl",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Post_PublishedAt",
                table: "Post",
                column: "PublishedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Post_Slug",
                table: "Post",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostEvent_PostId_Type",
                table: "PostEvent",
                columns: new[] { "PostId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_PostView_PostId_ViewedAt",
                table: "PostView",
                columns: new[] { "PostId", "ViewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostEvent");

            migrationBuilder.DropTable(
                name: "PostImage");

            migrationBuilder.DropTable(
                name: "PostTag");

            migrationBuilder.DropTable(
                name: "PostView");

            migrationBuilder.DropTable(
                name: "Post");
        }
    }
}
