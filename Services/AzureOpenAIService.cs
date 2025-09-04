using System.Text;
using System.Text.Json;
using WorkBot.Models;

namespace WorkBot.Services
{
    public class AzureOpenAIService : IAzureOpenAIService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public AzureOpenAIService(HttpClient httpClient, IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> GenerateResponseAsync(List<MessageDto> conversationHistory)
        {
            try
            {
                var endpoint = _configuration["AzureOpenAI:Endpoint"];
                var apiKey = _configuration["AzureOpenAI:ApiKey"];
                var deploymentName = _configuration["AzureOpenAI:DeploymentName"];
                var apiVersion = _configuration["AzureOpenAI:ApiVersion"];

                var url = $"{endpoint}/openai/deployments/{deploymentName}/chat/completions?api-version={apiVersion}";

                var messages = new List<object>
                {
                    new { role = "system", content = "You are WorkBot, a helpful AI assistant with long-term memory and file processing capabilities. When users upload files, analyze them thoroughly and answer questions about their content." }
                };

                messages.AddRange(conversationHistory.Select(m => new { role = m.Role, content = m.Content }));

                var requestBody = new
                {
                    messages = messages,
                    max_tokens = 4000,
                    temperature = 0.7
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Azure OpenAI API error: {response.StatusCode} - {responseContent}");
                }

                using var jsonDoc = JsonDocument.Parse(responseContent);
                var choices = jsonDoc.RootElement.GetProperty("choices");
                if (choices.GetArrayLength() > 0)
                {
                    var message = choices[0].GetProperty("message");
                    return message.GetProperty("content").GetString() ?? "No response generated.";
                }

                return "No response generated.";
            }
            catch (Exception ex)
            {
                return $"I'm experiencing technical difficulties: {ex.Message}";
            }
        }
    }
}