using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IAzureOpenAIService
    {
        Task<string> GenerateResponseAsync(List<MessageDto> conversationHistory);
    }
}