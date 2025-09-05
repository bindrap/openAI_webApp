using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IAzureOpenAIService
    {
        Task<string> GenerateResponseAsync(List<MessageDto> conversationHistory);
        string GetModelName(); // Add this method to get current model name
    }
}