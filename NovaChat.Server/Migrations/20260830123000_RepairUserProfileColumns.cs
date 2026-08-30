using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NovaChat.Server.Migrations;

public partial class RepairUserProfileColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"Bio\" text NOT NULL DEFAULT '';");
        migrationBuilder.Sql("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"AvatarUrl\" text NULL;");
        migrationBuilder.Sql("ALTER TABLE \"Users\" ADD COLUMN IF NOT EXISTS \"LastSeenAt\" timestamp with time zone NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"Users\" DROP COLUMN IF EXISTS \"LastSeenAt\";");
        migrationBuilder.Sql("ALTER TABLE \"Users\" DROP COLUMN IF EXISTS \"AvatarUrl\";");
        migrationBuilder.Sql("ALTER TABLE \"Users\" DROP COLUMN IF EXISTS \"Bio\";");
    }
}
