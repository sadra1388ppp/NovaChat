using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Entities;
namespace NovaChat.Server.Data;
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }
    public DbSet<User> Users { get; set; } = null!;
    public DbSet<Chat> Chats { get; set; } = null!;
    public DbSet<ChatMember> ChatMembers { get; set; } = null!;
    public DbSet<Message> Messages { get; set; } = null!;
    public DbSet<Contact> Contacts { get; set; } = null!;
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>().Property(u => u.Id).HasColumnType("varchar(255)").HasConversion<string>().ValueGeneratedNever();
        modelBuilder.Entity<User>().Property(u => u.Username).HasMaxLength(32).IsRequired();
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<User>().Property(u => u.PhoneNumber).HasMaxLength(32);
        modelBuilder.Entity<User>().HasIndex(u => u.PhoneNumber).IsUnique();
        modelBuilder.Entity<Chat>().Property(c => c.User1Id).HasColumnName("User1Id").HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Chat>().Property(c => c.User2Id).HasColumnName("User2Id").HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Chat>().Property(c => c.CreatedByUserId).HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Chat>().Property(c => c.Name).HasMaxLength(128).IsRequired();
        modelBuilder.Entity<Chat>().Property(c => c.Type).HasConversion<int>();
        modelBuilder.Entity<Chat>().HasOne(c => c.User1).WithMany().HasForeignKey(c => c.User1Id).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Chat>().HasOne(c => c.User2).WithMany().HasForeignKey(c => c.User2Id).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Chat>().HasOne(c => c.CreatedByUser).WithMany().HasForeignKey(c => c.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ChatMember>().Property(m => m.UserId).HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<ChatMember>().Property(m => m.Role).HasConversion<int>();
        modelBuilder.Entity<ChatMember>().HasOne(m => m.Chat).WithMany(c => c.Members).HasForeignKey(m => m.ChatId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChatMember>().HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChatMember>().HasIndex(m => new { m.ChatId, m.UserId }).IsUnique();
        modelBuilder.Entity<Message>().Property(m => m.SenderId).HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Message>().HasOne(m => m.Chat).WithMany(c => c.Messages).HasForeignKey(m => m.ChatId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Message>().HasOne(m => m.Sender).WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Contact>().Property(c => c.OwnerUserId).HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Contact>().Property(c => c.ContactUserId).HasColumnType("varchar(255)").HasConversion<string>();
        modelBuilder.Entity<Contact>().HasOne(c => c.OwnerUser).WithMany().HasForeignKey(c => c.OwnerUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Contact>().HasOne(c => c.ContactUser).WithMany().HasForeignKey(c => c.ContactUserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Contact>().HasIndex(c => new { c.OwnerUserId, c.ContactUserId }).IsUnique();
    }
}
