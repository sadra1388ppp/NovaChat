using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaChat.Server.Migrations
{
    public partial class AddMessageDeletion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeletedForEveryone",
                table: "Messages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeletedForUserIds",
                table: "Messages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "DeletedForEveryone",
                table: "GroupMessages",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DeletedForUserIds",
                table: "GroupMessages",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "DeletedForEveryone", table: "Messages");
            migrationBuilder.DropColumn(name: "DeletedForUserIds", table: "Messages");
            migrationBuilder.DropColumn(name: "DeletedForEveryone", table: "GroupMessages");
            migrationBuilder.DropColumn(name: "DeletedForUserIds", table: "GroupMessages");
        }
    }
}