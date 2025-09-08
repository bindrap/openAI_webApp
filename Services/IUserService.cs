using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IUserService
    {
        // Legacy authentication methods (for backward compatibility)
        Task<User?> AuthenticateAsync(string username, string password);
        Task<CreateUserResult> CreateUserAsync(string username, string email, string password);
        
        // New Identity Server methods
        Task<User> EnsureUserExistsAsync(string externalId, string email, string? displayName = null, string? employeeId = null);
        Task<User?> GetUserByIdAsync(int userId);
        Task<User?> GetUserByExternalIdAsync(string externalId);
        
        // Common methods
        Task SignInAsync(HttpContext httpContext, User user);
        Task SignOutAsync(HttpContext httpContext);
    }
}