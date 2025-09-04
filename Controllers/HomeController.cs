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

        public HomeController(IConversationService conversationService)
        {
            _conversationService = conversationService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> NewConversation()
        {
            var userId = GetCurrentUserId();
            var conversationId = await _conversationService.CreateConversationAsync(userId);
            
            HttpContext.Session.SetString("ConversationId", conversationId);
            HttpContext.Session.SetString("SessionId", Guid.NewGuid().ToString());
            
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> LoadConversation(string id)
        {
            var userId = GetCurrentUserId();
            var conversation = await _conversationService.GetConversationAsync(id, userId);
            
            if (conversation == null)
            {
                TempData["Error"] = "Conversation not found";
                return RedirectToAction("Index");
            }

            HttpContext.Session.SetString("ConversationId", id);
            HttpContext.Session.SetString("SessionId", Guid.NewGuid().ToString());
            
            return RedirectToAction("Index");
        }

        private int GetCurrentUserId()
        {
            return int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        }
    }
}