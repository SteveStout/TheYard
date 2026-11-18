using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheYard.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class BlogReadModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The blog's instants move from text to their UTC ticks (BlogModel.UtcTicks). A file kept from before has
            // them as text, so each is turned into its ticks first: days since 0001-01-01 in UTC, julianday's reading
            // of the text with its offset, times the ticks in a day. The rebuild below then copies the numbers over.
            foreach (var (table, column) in new[] { ("Post", "PublishedAt"), ("Post", "CreatedAt"), ("Post", "UpdatedAt"), ("PostView", "ViewedAt"), ("PostEvent", "At") })
            {
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"{column}\" = CAST(ROUND((julianday(\"{column}\") - 1721425.5) * 864000000000) AS INTEGER) WHERE typeof(\"{column}\") = 'text';");
            }

            migrationBuilder.DropIndex(
                name: "IX_PostEvent_PostId_Type",
                table: "PostEvent");

            migrationBuilder.DropIndex(
                name: "IX_Post_PublishedAt",
                table: "Post");

            migrationBuilder.RenameIndex(
                name: "IX_PostView_PostId_ViewedAt",
                table: "PostView",
                newName: "IX_PostView_Reads");

            migrationBuilder.AlterColumn<long>(
                name: "ViewedAt",
                table: "PostView",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "At",
                table: "PostEvent",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "UpdatedAt",
                table: "Post",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "PublishedAt",
                table: "Post",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<long>(
                name: "CreatedAt",
                table: "Post",
                type: "INTEGER",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "BodyHtml",
                table: "Post",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DerivedWith",
                table: "Post",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadingMinutes",
                table: "Post",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PostKeyword",
                columns: table => new
                {
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Keyword = table.Column<string>(type: "TEXT", unicode: false, maxLength: 40, nullable: false),
                    Mentions = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostKeyword", x => new { x.PostId, x.Keyword });
                    table.ForeignKey(
                        name: "FK_PostKeyword_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PostWord",
                columns: table => new
                {
                    Word = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    PostId = table.Column<int>(type: "INTEGER", nullable: false),
                    Field = table.Column<byte>(type: "INTEGER", nullable: false),
                    Mentions = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PostWord", x => new { x.Word, x.PostId, x.Field });
                    table.ForeignKey(
                        name: "FK_PostWord_Post_PostId",
                        column: x => x.PostId,
                        principalTable: "Post",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SearchSynonym",
                columns: table => new
                {
                    Word = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Term = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SearchSynonym", x => new { x.Word, x.Term });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PostView_Window",
                table: "PostView",
                column: "ViewedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PostEvent_PostId",
                table: "PostEvent",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_PostEvent_Window",
                table: "PostEvent",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Post_Published",
                table: "Post",
                columns: new[] { "PublishedAt", "Id" },
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_PostKeyword_Keyword",
                table: "PostKeyword",
                columns: new[] { "Keyword", "PostId" });

            migrationBuilder.CreateIndex(
                name: "IX_PostWord_PostId",
                table: "PostWord",
                column: "PostId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PostKeyword");

            migrationBuilder.DropTable(
                name: "PostWord");

            migrationBuilder.DropTable(
                name: "SearchSynonym");

            migrationBuilder.DropIndex(
                name: "IX_PostView_Window",
                table: "PostView");

            migrationBuilder.DropIndex(
                name: "IX_PostEvent_PostId",
                table: "PostEvent");

            migrationBuilder.DropIndex(
                name: "IX_PostEvent_Window",
                table: "PostEvent");

            migrationBuilder.DropIndex(
                name: "IX_Post_Published",
                table: "Post");

            migrationBuilder.DropColumn(
                name: "BodyHtml",
                table: "Post");

            migrationBuilder.DropColumn(
                name: "DerivedWith",
                table: "Post");

            migrationBuilder.DropColumn(
                name: "ReadingMinutes",
                table: "Post");

            migrationBuilder.RenameIndex(
                name: "IX_PostView_Reads",
                table: "PostView",
                newName: "IX_PostView_PostId_ViewedAt");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "ViewedAt",
                table: "PostView",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "At",
                table: "PostEvent",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "Post",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "PublishedAt",
                table: "Post",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "Post",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_PostEvent_PostId_Type",
                table: "PostEvent",
                columns: new[] { "PostId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Post_PublishedAt",
                table: "Post",
                column: "PublishedAt");
        }
    }
}
