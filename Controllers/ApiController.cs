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
        private readonly IUserService _userService;
        private readonly ILogger<ApiController> _logger;

        public ApiController(
            IConversationService conversationService,
            IFileProcessingService fileService,
            IAzureOpenAIService aiService,
            IUserService userService,
            ILogger<ApiController> logger)
        {
            _conversationService = conversationService;
            _fileService = fileService;
            _aiService = aiService;
            _userService = userService;
            _logger = logger;
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
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
            {
                return Json(new { error = "User not found" });
            }

            var conversationId = HttpContext.Session.GetString("ConversationId");
            var sessionId = HttpContext.Session.GetString("SessionId") ?? Guid.NewGuid().ToString();

            try
            {
                // Ensure conversation exists
                if (string.IsNullOrEmpty(conversationId))
                {
                    conversationId = await _conversationService.CreateConversationAsync(userId.Value);
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
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
            {
                return Json(new { error = "User not found" });
            }

            var conversations = await _conversationService.GetUserConversationsAsync(userId.Value);
            
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
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
            {
                return Json(new { success = false, error = "User not found" });
            }
            
            try
            {
                _logger.LogInformation("[DELETE_CONV_API] Delete request for conversation {ConversationId} by user {UserId}", 
                    conversationId, userId);
                
                var success = await _conversationService.DeleteConversationAsync(conversationId, userId.Value);
                
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
                _logger.LogError(ex, "[DELETE_CONV_API] Error deleting conversation");
                return Json(new { success = false, error = $"Error deleting conversation: {ex.Message}" });
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

        private async Task<int?> GetCurrentUserIdAsync()
        {
            try
            {
                var nameIdentifierClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
                if (string.IsNullOrEmpty(nameIdentifierClaim))
                {
                    _logger.LogWarning("[GET_USER_ID] No NameIdentifier claim found");
                    return null;
                }

                _logger.LogInformation("[GET_USER_ID] NameIdentifier claim: {NameIdentifier}", nameIdentifierClaim);

                // Try to parse as integer first (for local users)
                if (int.TryParse(nameIdentifierClaim, out int localUserId))
                {
                    _logger.LogInformation("[GET_USER_ID] Found local user ID: {UserId}", localUserId);
                    return localUserId;
                }

                // If it's not an integer, it's probably a GUID from Identity Server
                // We need to look up the user by their external ID
                _logger.LogInformation("[GET_USER_ID] External ID detected, looking up user: {ExternalId}", nameIdentifierClaim);
                
                var user = await _userService.GetUserByExternalIdAsync(nameIdentifierClaim);
                if (user != null)
                {
                    _logger.LogInformation("[GET_USER_ID] Found user by external ID: {UserId}", user.Id);
                    return user.Id;
                }

                _logger.LogWarning("[GET_USER_ID] User not found for external ID: {ExternalId}", nameIdentifierClaim);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GET_USER_ID] Error getting current user ID");
                return null;
            }
        }
    }
}