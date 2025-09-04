using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WorkBot.Data
{
    public class WorkBotDbContext : DbContext
    {
        public WorkBotDbContext(DbContextOptions<WorkBotDbContext> options) : base(options) { }

        public DbSet<User> Users { get; set; }
        public DbSet<Conversation> Conversations { get; set; }
        public DbSet<Message> Messages { get; set; }
        public DbSet<SessionFile> SessionFiles { get; set; }
        public DbSet<Memory> Memories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            modelBuilder.Entity<Conversation>()
                .HasOne(c => c.User)
                .WithMany(u => u.Conversations)
                .HasForeignKey(c => c.UserId);

            modelBuilder.Entity<Message>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Messages)
                .HasForeignKey(m => m.ConversationId);

            modelBuilder.Entity<Memory>()
                .HasOne(m => m.Conversation)
                .WithMany(c => c.Memories)
                .HasForeignKey(m => m.ConversationId);
        }
    }

    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastLogin { get; set; }
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Conversation> Conversations { get; set; } = new List<Conversation>();
    }

    public class Conversation
    {
        [Key]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        public int UserId { get; set; }
        public virtual User User { get; set; } = null!;

        [MaxLength(200)]
        public string Title { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string SystemPrompt { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public int TotalMessages { get; set; } = 0;
        public DateTime LastMemoryConsolidation { get; set; } = DateTime.MinValue;
        public bool IsActive { get; set; } = true;

        public virtual ICollection<Message> Messages { get; set; } = new List<Message>();
        public virtual ICollection<Memory> Memories { get; set; } = new List<Memory>();
    }

    public class Message
    {
        [Key]
        public int Id { get; set; }

        public string ConversationId { get; set; } = string.Empty;
        public virtual Conversation Conversation { get; set; } = null!;

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = string.Empty; // "user" or "assistant"

        [Required]
        public string Content { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int Tokens { get; set; } = 0;
        public string MessageHash { get; set; } = string.Empty;
        public bool HasFiles { get; set; } = false;
    }

    public class Memory
    {
        [Key]
        public int Id { get; set; }

        public string ConversationId { get; set; } = string.Empty;
        public virtual Conversation Conversation { get; set; } = null!;

        [Required]
        public string Summary { get; set; } = string.Empty;

        public double ImportanceScore { get; set; } = 1.0;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastAccessed { get; set; } = DateTime.UtcNow;
    }

    public class SessionFile
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string SessionId { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string OriginalFilename { get; set; } = string.Empty;

        [Required]
        [MaxLength(255)]
        public string StoredFilename { get; set; } = string.Empty;

        public string FileHash { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string MimeType { get; set; } = string.Empty;
        public string ExtractedText { get; set; } = string.Empty;
        public DateTime UploadTime { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}