using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaChat.Server.Migrations;

public partial class ExpandUserProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Bio",
            table: "Users",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "AvatarUrl",
            table: "Users",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastSeenAt",
            table: "Users",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Bio", table: "Users");
        migrationBuilder.DropColumn(name: "AvatarUrl", table: "Users");
        migrationBuilder.DropColumn(name: "LastSeenAt", table: "Users");
    }
}