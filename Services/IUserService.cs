// Services/IUserService.cs
using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IUserService
    {
        Task<User?> AuthenticateAsync(string username, string password);
        Task<CreateUserResult> CreateUserAsync(string username, string email, string password);
        Task SignInAsync(HttpContext httpContext, User user);
        Task SignOutAsync(HttpContext httpContext);
    }
}