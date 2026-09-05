using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Chat> Chats { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // User.Id is an internal database identifier. The existing MariaDB
        // schema stores it as VARCHAR, so EF must never expect MariaDB to
        // auto-generate it. UserService assigns the value before insert.
        modelBuilder.Entity<User>()
            .Property(u => u.Id)
            .HasColumnType("varchar(255)")
            .HasConversion<string>()
            .ValueGeneratedNever();

        modelBuilder.Entity<User>()
            .Property(u => u.Username)
            .HasMaxLength(32)
            .IsRequired();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .Property(u => u.PhoneNumber)
            .HasMaxLength(32);

        modelBuilder.Entity<User>()
            .HasIndex(u => u.PhoneNumber)
            .IsUnique();

        // Keep the existing Chats column names from the database.
        // EF conventions map these properties to User1Id and User2Id.
        modelBuilder.Entity<Chat>()
            .Property(c => c.User1Id)
            .HasColumnType("varchar(255)")
            .HasConversion<string>();

        modelBuilder.Entity<Chat>()
            .Property(c => c.User2Id)
            .HasColumnType("varchar(255)")
            .HasConversion<string>();

        modelBuilder.Entity<Chat>()
            .HasOne(c => c.User1)
            .WithMany()
            .HasForeignKey(c => c.User1Id)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Chat>()
            .HasOne(c => c.User2)
            .WithMany()
            .HasForeignKey(c => c.User2Id)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Message>()
            .Property(m => m.SenderId)
            .HasColumnType("varchar(255)")
            .HasConversion<string>();

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Chat)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ChatId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Contact>()
            .Property(c => c.OwnerUserId)
            .HasColumnType("varchar(255)")
            .HasConversion<string>();

        modelBuilder.Entity<Contact>()
            .Property(c => c.ContactUserId)
            .HasColumnType("varchar(255)")
            .HasConversion<string>();

        modelBuilder.Entity<Contact>()
            .HasOne(c => c.OwnerUser)
            .WithMany()
            .HasForeignKey(c => c.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Contact>()
            .HasOne(c => c.ContactUser)
            .WithMany()
            .HasForeignKey(c => c.ContactUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Contact>()
            .HasIndex(c => new { c.OwnerUserId, c.ContactUserId })
            .IsUnique();
    }
}
