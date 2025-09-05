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
        Task<bool> DeleteConversationAsync(string conversationId, int userId);

         // Add token management methods
        Task<TokenUsageDto> GetConversationTokenUsageAsync(string conversationId);
        Task<List<MessageDto>> GetMessagesWithinTokenLimitAsync(string conversationId, int maxTokens = 24000);
        int EstimateTokens(string text);
        Task<int> TrimConversationHistoryAsync(string conversationId, int targetTokens = 20000);
    }
}