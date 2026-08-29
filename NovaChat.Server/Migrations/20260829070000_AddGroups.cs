using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace NovaChat.Server.Migrations;

public partial class AddGroups : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Groups",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Name = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                CreatorId = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Groups", x => x.Id);
                table.ForeignKey("FK_Groups_Users_CreatorId", x => x.CreatorId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GroupMembers",
            columns: table => new
            {
                GroupId = table.Column<int>(type: "integer", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                Role = table.Column<string>(type: "text", nullable: false),
                JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMembers", x => new { x.GroupId, x.UserId });
                table.ForeignKey("FK_GroupMembers_Groups_GroupId", x => x.GroupId, "Groups", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_GroupMembers_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "GroupMessages",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false).Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                GroupId = table.Column<int>(type: "integer", nullable: false),
                SenderId = table.Column<string>(type: "text", nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GroupMessages", x => x.Id);
                table.ForeignKey("FK_GroupMessages_Groups_GroupId", x => x.GroupId, "Groups", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_GroupMessages_Users_SenderId", x => x.SenderId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_Groups_CreatorId", "Groups", "CreatorId");
        migrationBuilder.CreateIndex("IX_GroupMembers_UserId", "GroupMembers", "UserId");
        migrationBuilder.CreateIndex("IX_GroupMessages_GroupId", "GroupMessages", "GroupId");
        migrationBuilder.CreateIndex("IX_GroupMessages_SenderId", "GroupMessages", "SenderId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("GroupMessages");
        migrationBuilder.DropTable("GroupMembers");
        migrationBuilder.DropTable("Groups");
    }
}