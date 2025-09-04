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

                // Save user message
                await _conversationService.SaveMessageAsync(conversationId, "user", completeMessage, hasFiles);

                // Get conversation context and generate response
                var conversationHistory = await _conversationService.GetRecentMessagesAsync(conversationId);
                var response = await _aiService.GenerateResponseAsync(conversationHistory);

                // Save assistant response
                await _conversationService.SaveMessageAsync(conversationId, "assistant", response);
                await _conversationService.UpdateConversationTimestampAsync(conversationId);

                return Json(new { reply = response });
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
                return Json(new { recent = new List<object>(), files = new List<object>() });
            }

            var recent = await _conversationService.GetRecentMessagesAsync(conversationId);
            
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

            return Json(new { recent, files, conversationId });
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