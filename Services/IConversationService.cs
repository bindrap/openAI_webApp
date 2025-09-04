using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IConversationService
    {
        Task<string> CreateConversationAsync(int userId, string? title = null);
        Task<Conversation?> GetConversationAsync(string conversationId, int userId);
        Task<List<Conversation>> GetUserConversationsAsync(int userId);
        Task<List<MessageDto>> GetRecentMessagesAsync(string conversationId, int limit = 30);
        Task SaveMessageAsync(string conversationId, string role, string content, bool hasFiles = false);
        Task UpdateConversationTimestampAsync(string conversationId);
    }
}