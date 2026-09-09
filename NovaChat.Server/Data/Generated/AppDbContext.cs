using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Chat> Chats { get; set; }

    public virtual DbSet<ChatMember> ChatMembers { get; set; }

    public virtual DbSet<Contact> Contacts { get; set; }

    public virtual DbSet<Message> Messages { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_unicode_ci")
            .HasCharSet("utf8mb4");

        modelBuilder.Entity<Chat>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.HasIndex(e => e.CreatedByUserId, "IX_Chats_CreatedByUserId");

            entity.HasIndex(e => new { e.Type, e.User1Id, e.User2Id }, "IX_Chats_Type_User1Id_User2Id");

            entity.HasIndex(e => e.User1Id, "IX_Chats_User1Id");

            entity.HasIndex(e => e.User2Id, "IX_Chats_User2Id");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Property(e => e.CreatedByUserId).HasColumnType("bigint(20)");
            entity.Property(e => e.Name).HasMaxLength(128);
            entity.Property(e => e.Type).HasColumnType("int(11)");
            entity.Property(e => e.User1Id).HasColumnType("bigint(20)");
            entity.Property(e => e.User2Id).HasColumnType("bigint(20)");

            entity.HasOne(d => d.CreatedByUser).WithMany(p => p.ChatCreatedByUsers).HasForeignKey(d => d.CreatedByUserId);

            entity.HasOne(d => d.User1).WithMany(p => p.ChatUser1s)
                .HasForeignKey(d => d.User1Id)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.User2).WithMany(p => p.ChatUser2s)
                .HasForeignKey(d => d.User2Id)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatMember>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.HasIndex(e => new { e.ChatId, e.UserId }, "IX_ChatMembers_ChatId_UserId").IsUnique();

            entity.HasIndex(e => e.UserId, "IX_ChatMembers_UserId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ChatId).HasColumnType("int(11)");
            entity.Property(e => e.JoinedAt).HasMaxLength(6);
            entity.Property(e => e.Role).HasColumnType("int(11)");
            entity.Property(e => e.UserId).HasColumnType("bigint(20)");

            entity.HasOne(d => d.Chat).WithMany(p => p.ChatMembers).HasForeignKey(d => d.ChatId);

            entity.HasOne(d => d.User).WithMany(p => p.ChatMembers).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.HasIndex(e => e.ContactUserId, "IX_Contacts_ContactUserId");

            entity.HasIndex(e => new { e.OwnerUserId, e.ContactUserId }, "IX_Contacts_OwnerUserId_ContactUserId").IsUnique();

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ContactUserId).HasColumnType("bigint(20)");
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Property(e => e.OwnerUserId).HasColumnType("bigint(20)");

            entity.HasOne(d => d.ContactUser).WithMany(p => p.ContactContactUsers).HasForeignKey(d => d.ContactUserId);

            entity.HasOne(d => d.OwnerUser).WithMany(p => p.ContactOwnerUsers).HasForeignKey(d => d.OwnerUserId);
        });

        modelBuilder.Entity<Message>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.HasIndex(e => new { e.ChatId, e.SentAt, e.Id }, "IX_Messages_ChatId_SentAt_Id");

            entity.HasIndex(e => e.SenderId, "IX_Messages_SenderId");

            entity.Property(e => e.Id).HasColumnType("int(11)");
            entity.Property(e => e.ChatId).HasColumnType("int(11)");
            entity.Property(e => e.SenderId).HasColumnType("bigint(20)");
            entity.Property(e => e.SentAt).HasMaxLength(6);

            entity.HasOne(d => d.Chat).WithMany(p => p.Messages).HasForeignKey(d => d.ChatId);

            entity.HasOne(d => d.Sender).WithMany(p => p.Messages).HasForeignKey(d => d.SenderId);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.HasIndex(e => e.Email, "IX_Users_Email").IsUnique();

            entity.HasIndex(e => e.PhoneNumber, "IX_Users_PhoneNumber").IsUnique();

            entity.HasIndex(e => e.Username, "IX_Users_Username").IsUnique();

            entity.Property(e => e.Id).HasColumnType("bigint(20)");
            entity.Property(e => e.AvatarUrl).HasMaxLength(512);
            entity.Property(e => e.Bio).HasMaxLength(160);
            entity.Property(e => e.CreatedAt).HasMaxLength(6);
            entity.Property(e => e.DisplayName).HasMaxLength(50);
            entity.Property(e => e.Email).HasMaxLength(254);
            entity.Property(e => e.LastSeenAt).HasMaxLength(6);
            entity.Property(e => e.PasswordHash).HasMaxLength(512);
            entity.Property(e => e.PhoneNumber).HasMaxLength(32);
            entity.Property(e => e.Username).HasMaxLength(32);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
