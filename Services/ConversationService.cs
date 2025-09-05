using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public class ConversationService : IConversationService
    {
        private readonly WorkBotDbContext _context;
        private const int DEFAULT_MAX_TOKENS = 24000;
        private const int SYSTEM_PROMPT_TOKENS = 50; // Rough estimate for system prompt
        private const int RESPONSE_BUFFER_TOKENS = 4000; // Reserve tokens for AI response

        public ConversationService(WorkBotDbContext context)
        {
            _context = context;
        }

        public async Task<string> CreateConversationAsync(int userId, string? title = null)
        {
            var conversation = new Conversation
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Title = title ?? $"Chat {DateTime.Now:yyyy-MM-dd HH:mm}",
                SystemPrompt = "You are WorkBot, a helpful AI assistant with long-term memory and file processing capabilities.",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Conversations.Add(conversation);
            await _context.SaveChangesAsync();

            return conversation.Id;
        }

        public async Task<Conversation?> GetConversationAsync(string conversationId, int userId)
        {
            return await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);
        }

        public async Task<List<Conversation>> GetUserConversationsAsync(int userId)
        {
            return await _context.Conversations
                .Where(c => c.UserId == userId && c.IsActive)
                .OrderByDescending(c => c.UpdatedAt)
                .Take(50)
                .ToListAsync();
        }

        public async Task<List<MessageDto>> GetRecentMessagesAsync(string conversationId, int limit = 30)
        {
            return await GetMessagesWithinTokenLimitAsync(conversationId, DEFAULT_MAX_TOKENS - RESPONSE_BUFFER_TOKENS);
        }

        // Add the missing method from the interface
        public async Task<List<MessageDto>> GetMessagesWithinTokenLimitAsync(string conversationId, int maxTokens = 24000)
        {
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Id)
                .Take(100) // Get more messages to ensure we have enough to work with
                .ToListAsync();

            var result = new List<MessageDto>();
            var totalTokens = SYSTEM_PROMPT_TOKENS; // Account for system prompt

            foreach (var message in messages.AsEnumerable().Reverse())
            {
                var messageTokens = message.Tokens > 0 ? message.Tokens : EstimateTokens(message.Content);
                
                if (totalTokens + messageTokens > maxTokens)
                {
                    // If this is the first message and it's too large, include it anyway but truncate
                    if (result.Count == 0)
                    {
                        var truncatedContent = TruncateToTokenLimit(message.Content, maxTokens - totalTokens);
                        result.Add(new MessageDto
                        {
                            Role = message.Role,
                            Content = truncatedContent,
                            HasFiles = message.HasFiles,
                            Tokens = EstimateTokens(truncatedContent)
                        });
                    }
                    break;
                }

                result.Add(new MessageDto
                {
                    Role = message.Role,
                    Content = message.Content,
                    HasFiles = message.HasFiles,
                    Tokens = messageTokens
                });

                totalTokens += messageTokens;
            }

            return result;
        }
        
        public async Task<TokenUsageDto> GetConversationTokenUsageAsync(string conversationId)
        {
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Id)
                .Take(50)
                .ToListAsync();

            var currentTokens = SYSTEM_PROMPT_TOKENS;
            foreach (var message in messages)
            {
                currentTokens += message.Tokens > 0 ? message.Tokens : EstimateTokens(message.Content);
            }

            var maxTokens = DEFAULT_MAX_TOKENS - RESPONSE_BUFFER_TOKENS;
            var usagePercentage = (double)currentTokens / maxTokens * 100;

            return new TokenUsageDto
            {
                CurrentTokens = currentTokens,
                MaxTokens = maxTokens,
                UsagePercentage = Math.Round(usagePercentage, 1),
                IsNearLimit = usagePercentage > 75,
                IsAtLimit = usagePercentage > 90,
                RemainingTokens = Math.Max(0, maxTokens - currentTokens)
            };
        }

        public async Task<int> TrimConversationHistoryAsync(string conversationId, int targetTokens = 20000)
        {
            Console.WriteLine($"[TRIM_HISTORY] Starting trim for conversation {conversationId}, target: {targetTokens} tokens");
            
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderBy(m => m.Id) // Oldest first
                .ToListAsync();

            var totalTokens = SYSTEM_PROMPT_TOKENS;
            var messagesToKeep = new List<Message>();
            var removedCount = 0;

            // Calculate current total
            foreach (var message in messages)
            {
                totalTokens += message.Tokens > 0 ? message.Tokens : EstimateTokens(message.Content);
            }

            Console.WriteLine($"[TRIM_HISTORY] Current total tokens: {totalTokens}");

            if (totalTokens <= targetTokens)
            {
                Console.WriteLine($"[TRIM_HISTORY] No trimming needed");
                return 0;
            }

            // Start from the end (most recent) and work backwards
            var currentTotal = SYSTEM_PROMPT_TOKENS;
            for (int i = messages.Count - 1; i >= 0; i--)
            {
                var message = messages[i];
                var messageTokens = message.Tokens > 0 ? message.Tokens : EstimateTokens(message.Content);
                
                if (currentTotal + messageTokens <= targetTokens)
                {
                    messagesToKeep.Insert(0, message); // Insert at beginning to maintain order
                    currentTotal += messageTokens;
                }
                else
                {
                    // Mark older messages for deletion (soft delete by keeping them but not including in conversations)
                    removedCount++;
                }
            }

            // We don't actually delete messages, just rely on the GetMessagesWithinTokenLimitAsync method
            // to naturally exclude older messages. This preserves the data while managing token usage.
            
            Console.WriteLine($"[TRIM_HISTORY] Would remove {removedCount} messages, keeping {messagesToKeep.Count}");
            Console.WriteLine($"[TRIM_HISTORY] New total tokens: {currentTotal}");

            return removedCount;
        }

        public async Task SaveMessageAsync(string conversationId, string role, string content, bool hasFiles = false)
        {
            // Fix: Calculate tokens before using it
            var tokens = EstimateTokens(content);
            
            var message = new Message
            {
                ConversationId = conversationId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tokens = tokens, // Fix: Now tokens is defined
                MessageHash = ComputeHash(content),
                HasFiles = hasFiles
            };

            _context.Messages.Add(message);

            // Update conversation stats
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId);

            if (conversation != null)
            {
                conversation.TotalMessages++;
                conversation.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
        }

        public async Task UpdateConversationTimestampAsync(string conversationId)
        {
            var conversation = await _context.Conversations
                .FirstOrDefaultAsync(c => c.Id == conversationId);
            
            if (conversation != null)
            {
                conversation.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> DeleteConversationAsync(string conversationId, int userId)
        {
            try
            {
                Console.WriteLine($"[DELETE_CONVERSATION] Attempting to delete conversation {conversationId} for user {userId}");
                
                var conversation = await _context.Conversations
                    .FirstOrDefaultAsync(c => c.Id == conversationId && c.UserId == userId && c.IsActive);

                if (conversation == null)
                {
                    Console.WriteLine($"[DELETE_CONVERSATION] Conversation not found or access denied");
                    return false;
                }

                // Soft delete - mark as inactive instead of hard delete
                conversation.IsActive = false;
                conversation.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                
                Console.WriteLine($"[DELETE_CONVERSATION] Successfully deleted conversation {conversationId}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE_CONVERSATION] Error: {ex.Message}");
                return false;
            }
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            
            // More accurate token estimation based on OpenAI's approximation
            // Average of 4 characters per token, but account for spaces and punctuation
            var characterCount = text.Length;
            var wordCount = text.Split(new char[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
            
            // Use a combination of character and word count for better estimation
            var tokenEstimate = Math.Max(characterCount / 4, wordCount * 1.3);
            
            return (int)Math.Ceiling(tokenEstimate);
        }

        private string TruncateToTokenLimit(string content, int maxTokens)
        {
            if (EstimateTokens(content) <= maxTokens) return content;
            
            // Estimate characters per token and truncate accordingly
            var avgCharsPerToken = 4;
            var maxChars = maxTokens * avgCharsPerToken;
            
            if (content.Length <= maxChars) return content;
            
            // Truncate and add indication
            var truncated = content.Substring(0, Math.Max(0, maxChars - 50));
            return truncated + "... [message truncated due to token limit]";
        }

        private string ComputeHash(string content)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash);
        }
    }
}