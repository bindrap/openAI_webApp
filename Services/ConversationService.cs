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
            const int maxTokens = 24000;
            
            var messages = await _context.Messages
                .Where(m => m.ConversationId == conversationId)
                .OrderByDescending(m => m.Id)
                .Take(limit)
                .ToListAsync();

            var result = new List<MessageDto>();
            var totalTokens = 0;

            foreach (var message in messages.AsEnumerable().Reverse())
            {
                var tokens = message.Tokens > 0 ? message.Tokens : EstimateTokens(message.Content);
                
                if (totalTokens + tokens > maxTokens)
                    break;

                result.Add(new MessageDto
                {
                    Role = message.Role,
                    Content = message.Content,
                    HasFiles = message.HasFiles
                });

                totalTokens += tokens;
            }

            return result;
        }

        public async Task SaveMessageAsync(string conversationId, string role, string content, bool hasFiles = false)
        {
            var message = new Message
            {
                ConversationId = conversationId,
                Role = role,
                Content = content,
                CreatedAt = DateTime.UtcNow,
                Tokens = EstimateTokens(content),
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

        private int EstimateTokens(string text)
        {
            return text.Length / 4; // Rough estimation
        }

        private string ComputeHash(string content)
        {
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(content));
            return Convert.ToHexString(hash);
        }
    }
}