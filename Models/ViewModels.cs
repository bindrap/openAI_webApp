using System.ComponentModel.DataAnnotations;

namespace WorkBot.Models
{
    public class LoginViewModel
    {
        [Required]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }

    public class RegisterViewModel
    {
        [Required]
        [StringLength(50, MinimumLength = 3)]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100, MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Confirm Password")]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class CreateUserResult
    {
        public bool Success { get; set; }
        public int UserId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class MessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool HasFiles { get; set; }
        public int Tokens { get; set; }
    }

    public class SessionFileDto
    {
        public int Id { get; set; }
        public string OriginalFilename { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
    }

    public class ConversationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int MessageCount { get; set; }
    }

    // Add new DTOs for token management
    public class TokenUsageDto
    {
        public int CurrentTokens { get; set; }
        public int MaxTokens { get; set; }
        public double UsagePercentage { get; set; }
        public bool IsNearLimit { get; set; }
        public bool IsAtLimit { get; set; }
        public int EstimatedInputTokens { get; set; }
        public int RemainingTokens { get; set; }
    }

    public class ChatResponseDto
    {
        public string Reply { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public TokenUsageDto TokenUsage { get; set; } = new();
        public bool TrimmedHistory { get; set; }
        public int MessagesRemoved { get; set; }
        public string? Warning { get; set; }
    }
}