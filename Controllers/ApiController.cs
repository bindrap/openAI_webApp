using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkBot.Services;
using WorkBot.Models;
using System.Security.Claims;

namespace WorkBot.Controllers
{
    [Authorize]
    public class ApiController : Controller
    {
        private readonly IConversationService _conversationService;
        private readonly IFileProcessingService _fileService;
        private readonly IAzureOpenAIService _aiService;

        public ApiController(
            IConversationService conversationService,
            IFileProcessingService fileService,
            IAzureOpenAIService aiService)
        {
            _conversationService = conversationService;
            _fileService = fileService;
            _aiService = aiService;
        }

        [HttpPost]
        public async Task<IActionResult> UploadFiles(List<IFormFile> files)
        {
            var sessionId = HttpContext.Session.GetString("SessionId");
            if (string.IsNullOrEmpty(sessionId))
            {
                sessionId = Guid.NewGuid().ToString();
                HttpContext.Session.SetString("SessionId", sessionId);
            }

            try
            {
                var uploadedFiles = await _fileService.SaveSessionFilesAsync(sessionId, files);
                return Json(new { files = uploadedFiles, total = uploadedFiles.Count });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> RemoveFile(int id)
        {
            var sessionId = HttpContext.Session.GetString("SessionId");
            if (string.IsNullOrEmpty(sessionId))
                return Json(new { error = "No active session" });

            try
            {
                await _fileService.RemoveSessionFileAsync(sessionId, id);
                return Json(new { success = true });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ClearFiles()
        {
            var sessionId = HttpContext.Session.GetString("SessionId");
            if (!string.IsNullOrEmpty(sessionId))
            {
                await _fileService.ClearSessionFilesAsync(sessionId);
            }
            return Json(new { success = true });
        }

        [HttpPost]
        public async Task<IActionResult> Chat([FromForm] string message)
        {
            var userId = GetCurrentUserId();
            var conversationId = HttpContext.Session.GetString("ConversationId");
            var sessionId = HttpContext.Session.GetString("SessionId") ?? Guid.NewGuid().ToString();

            try
            {
                // Ensure conversation exists
                if (string.IsNullOrEmpty(conversationId))
                {
                    conversationId = await _conversationService.CreateConversationAsync(userId);
                    HttpContext.Session.SetString("ConversationId", conversationId);
                }

                // Get session files
                var sessionFiles = await _fileService.GetSessionFilesAsync(sessionId);
                var hasFiles = sessionFiles.Any();

                // Build complete message
                var completeMessage = BuildCompleteMessage(message, sessionFiles);
                
                if (string.IsNullOrWhiteSpace(completeMessage))
                {
                    return Json(new { error = "No message or files provided" });
                }

                // Estimate tokens for the new message
                var messageTokens = _conversationService.EstimateTokens(completeMessage);
                
                // Check current token usage
                var tokenUsage = await _conversationService.GetConversationTokenUsageAsync(conversationId);
                var projectedTokens = tokenUsage.CurrentTokens + messageTokens + 4000; // Reserve for response

                var trimmedHistory = false;
                var messagesRemoved = 0;
                string? warning = null;

                // If approaching limit, trim history
                if (projectedTokens > tokenUsage.MaxTokens)
                {
                    Console.WriteLine($"[CHAT] Token limit approaching. Current: {tokenUsage.CurrentTokens}, Message: {messageTokens}, Projected: {projectedTokens}");
                    
                    messagesRemoved = await _conversationService.TrimConversationHistoryAsync(conversationId, 16000);
                    trimmedHistory = messagesRemoved > 0;
                    
                    if (trimmedHistory)
                    {
                        warning = $"Conversation history was trimmed ({messagesRemoved} older messages removed) to stay within token limits.";
                        Console.WriteLine($"[CHAT] Trimmed {messagesRemoved} messages from conversation history");
                    }
                }

                // Check if message itself is too large
                if (messageTokens > 15000) // Leave room for response
                {
                    return Json(new { 
                        error = $"Your message is too long ({messageTokens} tokens). Please reduce it to under 15,000 tokens.",
                        tokenUsage = await _conversationService.GetConversationTokenUsageAsync(conversationId)
                    });
                }

                // Save user message
                await _conversationService.SaveMessageAsync(conversationId, "user", completeMessage, hasFiles);

                // Get conversation context with token limits applied
                var conversationHistory = await _conversationService.GetMessagesWithinTokenLimitAsync(conversationId);
                
                // Generate response
                var response = await _aiService.GenerateResponseAsync(conversationHistory);

                // Save assistant response
                await _conversationService.SaveMessageAsync(conversationId, "assistant", response);
                await _conversationService.UpdateConversationTimestampAsync(conversationId);

                // Get updated token usage
                var finalTokenUsage = await _conversationService.GetConversationTokenUsageAsync(conversationId);

                // Return comprehensive response
                return Json(new ChatResponseDto
                {
                    Reply = response,
                    Model = _aiService.GetModelName(),
                    TokenUsage = finalTokenUsage,
                    TrimmedHistory = trimmedHistory,
                    MessagesRemoved = messagesRemoved,
                    Warning = warning
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = $"AI service error: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> History()
        {
            var conversationId = HttpContext.Session.GetString("ConversationId");
            if (string.IsNullOrEmpty(conversationId))
            {
                return Json(new { 
                    recent = new List<object>(), 
                    files = new List<object>(),
                    tokenUsage = new TokenUsageDto { CurrentTokens = 0, MaxTokens = 20000, UsagePercentage = 0 }
                });
            }

            var recent = await _conversationService.GetRecentMessagesAsync(conversationId);
            var tokenUsage = await _conversationService.GetConversationTokenUsageAsync(conversationId);
            
            var sessionId = HttpContext.Session.GetString("SessionId");
            var files = new List<object>();
            if (!string.IsNullOrEmpty(sessionId))
            {
                var sessionFiles = await _fileService.GetSessionFilesAsync(sessionId);
                files = sessionFiles.Select(f => new { 
                    id = f.Id, 
                    name = f.OriginalFilename, 
                    size = f.FileSize 
                }).ToList<object>();
            }

            return Json(new { recent, files, conversationId, tokenUsage });
        }

        [HttpGet]
        public async Task<IActionResult> Conversations()
        {
            var userId = GetCurrentUserId();
            var conversations = await _conversationService.GetUserConversationsAsync(userId);
            
            return Json(new { 
                conversations = conversations.Select(c => new {
                    id = c.Id,
                    title = c.Title,
                    created_at = c.CreatedAt.ToString("yyyy-MM-dd HH:mm"),
                    updated_at = c.UpdatedAt.ToString("yyyy-MM-dd HH:mm"),
                    message_count = c.TotalMessages
                })
            });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteConversation([FromForm] string conversationId)
        {
            var userId = GetCurrentUserId();
            
            try
            {
                Console.WriteLine($"[DELETE_CONV_API] Delete request for conversation {conversationId} by user {userId}");
                
                var success = await _conversationService.DeleteConversationAsync(conversationId, userId);
                
                if (success)
                {
                    // If this was the current conversation, clear the session
                    var currentConversationId = HttpContext.Session.GetString("ConversationId");
                    if (currentConversationId == conversationId)
                    {
                        HttpContext.Session.Remove("ConversationId");
                        HttpContext.Session.Remove("SessionId");
                    }
                    
                    return Json(new { success = true, message = "Conversation deleted successfully" });
                }
                else
                {
                    return Json(new { success = false, error = "Conversation not found or access denied" });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DELETE_CONV_API] Error: {ex.Message}");
                return Json(new { success = false, error = $"Error deleting conversation: {ex.Message}" });
            }
        }

        // Get current token usage for a conversation
        [HttpGet]
        public async Task<IActionResult> GetTokenUsage()
        {
            var conversationId = HttpContext.Session.GetString("ConversationId");
            if (string.IsNullOrEmpty(conversationId))
            {
                return Json(new TokenUsageDto { CurrentTokens = 0, MaxTokens = 20000, UsagePercentage = 0 });
            }

            try
            {
                var tokenUsage = await _conversationService.GetConversationTokenUsageAsync(conversationId);
                return Json(tokenUsage);
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // Estimate tokens for input text
        [HttpPost]
        public IActionResult EstimateTokens([FromForm] string text)
        {
            try
            {
                var sessionFiles = new List<SessionFileDto>(); // Could get actual files if needed
                var completeMessage = BuildCompleteMessage(text, sessionFiles);
                var tokens = _conversationService.EstimateTokens(completeMessage);
                
                return Json(new { 
                    tokens = tokens, 
                    text = text,
                    textLength = text?.Length ?? 0,
                    isOverLimit = tokens > 15000
                });
            }
            catch (Exception ex)
            {
                return Json(new { error = ex.Message });
            }
        }

        // Get model info (existing method)
        [HttpGet]
        public IActionResult GetModelInfo()
        {
            try
            {
                return Json(new { 
                    model = _aiService.GetModelName(),
                    status = "online"
                });
            }
            catch (Exception ex)
            {
                return Json(new { 
                    model = "Unknown",
                    status = "error",
                    error = ex.Message
                });
            }
        }

        private string BuildCompleteMessage(string userMessage, List<SessionFileDto> sessionFiles)
        {
            var completeMessage = "";

            if (sessionFiles.Any())
            {
                completeMessage += "=== UPLOADED FILES ===\n\n";
                foreach (var file in sessionFiles)
                {
                    var fileSizeKb = file.FileSize / 1024;
                    completeMessage += $"File: {file.OriginalFilename} ({fileSizeKb} KB)\n";
                    completeMessage += $"Content:\n{file.ExtractedText}\n\n";
                }
                completeMessage += "=== END FILES ===\n\n";
            }

            if (!string.IsNullOrWhiteSpace(userMessage))
            {
                completeMessage += $"User Question/Message: {userMessage}";
            }

            return completeMessage;
        }

        private int GetCurrentUserId()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }
    }
}