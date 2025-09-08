using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using WorkBot.Services;
using System.Security.Claims;

namespace WorkBot.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly IConversationService _conversationService;
        private readonly IUserService _userService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(
            IConversationService conversationService, 
            IUserService userService,
            ILogger<HomeController> logger)
        {
            _conversationService = conversationService;
            _userService = userService;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> NewConversation()
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
            {
                TempData["Error"] = "User not found";
                return RedirectToAction("Index");
            }

            var conversationId = await _conversationService.CreateConversationAsync(userId.Value);
            
            HttpContext.Session.SetString("ConversationId", conversationId);
            HttpContext.Session.SetString("SessionId", Guid.NewGuid().ToString());
            
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> LoadConversation(string id)
        {
            var userId = await GetCurrentUserIdAsync();
            if (userId == null)
            {
                TempData["Error"] = "User not found";
                return RedirectToAction("Index");
            }

            var conversation = await _conversationService.GetConversationAsync(id, userId.Value);
            
            if (conversation == null)
            {
                TempData["Error"] = "Conversation not found";
                return RedirectToAction("Index");
            }

            HttpContext.Session.SetString("ConversationId", id);
            HttpContext.Session.SetString("SessionId", Guid.NewGuid().ToString());
            
            return RedirectToAction("Index");
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