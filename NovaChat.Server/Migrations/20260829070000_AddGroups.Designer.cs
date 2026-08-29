using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NovaChat.Server.Data;

#nullable disable

namespace NovaChat.Server.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260829070000_AddGroups")]
partial class AddGroups
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.0");
        modelBuilder.Entity("NovaChat.Server.Entities.Group", b => { b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer"); b.Property<string>("Name").IsRequired().HasColumnType("text"); b.Property<string>("Description").IsRequired().HasColumnType("text"); b.Property<string>("CreatorId").IsRequired().HasColumnType("text"); b.Property<DateTime>("CreatedAt").HasColumnType("timestamp with time zone"); b.HasKey("Id"); b.HasIndex("CreatorId"); b.ToTable("Groups"); });
        modelBuilder.Entity("NovaChat.Server.Entities.GroupMember", b => { b.Property<int>("GroupId").HasColumnType("integer"); b.Property<string>("UserId").HasColumnType("text"); b.Property<string>("Role").IsRequired().HasColumnType("text"); b.Property<DateTime>("JoinedAt").HasColumnType("timestamp with time zone"); b.HasKey("GroupId", "UserId"); b.HasIndex("UserId"); b.ToTable("GroupMembers"); });
        modelBuilder.Entity("NovaChat.Server.Entities.GroupMessage", b => { b.Property<int>("Id").ValueGeneratedOnAdd().HasColumnType("integer"); b.Property<int>("GroupId").HasColumnType("integer"); b.Property<string>("SenderId").IsRequired().HasColumnType("text"); b.Property<string>("Content").IsRequired().HasColumnType("text"); b.Property<DateTime>("SentAt").HasColumnType("timestamp with time zone"); b.HasKey("Id"); b.HasIndex("GroupId"); b.HasIndex("SenderId"); b.ToTable("GroupMessages"); });
        modelBuilder.Entity("NovaChat.Server.Entities.Group", b => b.HasOne("NovaChat.Server.Entities.User", "Creator").WithMany().HasForeignKey("CreatorId").OnDelete(DeleteBehavior.Restrict).IsRequired());
        modelBuilder.Entity("NovaChat.Server.Entities.GroupMember", b => { b.HasOne("NovaChat.Server.Entities.Group", "Group").WithMany("Members").HasForeignKey("GroupId").OnDelete(DeleteBehavior.Cascade).IsRequired(); b.HasOne("NovaChat.Server.Entities.User", "User").WithMany().HasForeignKey("UserId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
        modelBuilder.Entity("NovaChat.Server.Entities.GroupMessage", b => { b.HasOne("NovaChat.Server.Entities.Group", "Group").WithMany("Messages").HasForeignKey("GroupId").OnDelete(DeleteBehavior.Cascade).IsRequired(); b.HasOne("NovaChat.Server.Entities.User", "Sender").WithMany().HasForeignKey("SenderId").OnDelete(DeleteBehavior.Restrict).IsRequired(); });
    }
}