using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public class UserService : IUserService
    {
        private readonly WorkBotDbContext _context;
        private readonly ILogger<UserService> _logger;

        public UserService(WorkBotDbContext context, ILogger<UserService> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Legacy authentication method (keep for backward compatibility if needed)
        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            _logger.LogInformation("[AUTH] Legacy authentication attempt for user: {Username}", username);
            
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null)
            {
                _logger.LogWarning("[AUTH] User not found: {Username}", username);
                return null;
            }

            var isValidPassword = VerifyPassword(password, user.PasswordHash);
            _logger.LogInformation("[AUTH] Password valid: {IsValid}", isValidPassword);

            if (!isValidPassword)
                return null;

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[AUTH] Legacy authentication successful for: {Username}", username);
            return user;
        }

        // New method for Identity Server integration
        public async Task<User> EnsureUserExistsAsync(string externalId, string email, string? displayName = null, string? employeeId = null)
        {
            _logger.LogInformation("[IDENTITY] Ensuring user exists - ExternalId: {ExternalId}, Email: {Email}", externalId, email);
            
            // Try to find user by external ID first, then by email
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ExternalId == externalId || u.Email == email);

            if (user == null)
            {
                // Create new user
                _logger.LogInformation("[IDENTITY] Creating new user for ExternalId: {ExternalId}", externalId);
                
                user = new User
                {
                    ExternalId = externalId,
                    Username = email.Split('@')[0], // Use email prefix as username
                    Email = email,
                    DisplayName = displayName ?? email,
                    EmployeeId = employeeId,
                    PasswordHash = string.Empty, // No password needed for SSO users
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    AuthenticationMethod = "IdentityServer"
                };

                _context.Users.Add(user);
            }
            else
            {
                // Update existing user
                _logger.LogInformation("[IDENTITY] Updating existing user: {UserId}", user.Id);
                
                user.ExternalId = externalId;
                user.Email = email;
                user.DisplayName = displayName ?? user.DisplayName ?? email;
                user.EmployeeId = employeeId ?? user.EmployeeId;
                user.AuthenticationMethod = "IdentityServer";
            }

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[IDENTITY] User ensured successfully: {UserId}", user.Id);
            return user;
        }

        public async Task<CreateUserResult> CreateUserAsync(string username, string email, string password)
        {
            try
            {
                _logger.LogInformation("[CREATE_USER] Creating legacy user: {Username}, {Email}", username, email);
                
                // Check if username or email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

                if (existingUser != null)
                {
                    var field = existingUser.Username == username ? "Username" : "Email";
                    _logger.LogWarning("[CREATE_USER] User already exists - {Field}: {Username} / {Email}", field, existingUser.Username, existingUser.Email);
                    return new CreateUserResult { Success = false, ErrorMessage = $"{field} already exists" };
                }

                var hashedPassword = HashPassword(password);
                
                var user = new User
                {
                    Username = username,
                    Email = email,
                    DisplayName = username,
                    PasswordHash = hashedPassword,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    AuthenticationMethod = "Local"
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                _logger.LogInformation("[CREATE_USER] User created successfully with ID: {UserId}", user.Id);
                return new CreateUserResult { Success = true, UserId = user.Id };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CREATE_USER] Exception in CreateUser");
                return new CreateUserResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task SignInAsync(HttpContext httpContext, User user)
        {
            _logger.LogInformation("[SIGNIN] Signing in user: {Username}", user.Username);
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.DisplayName ?? user.Username),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("username", user.Username)
            };

            if (!string.IsNullOrEmpty(user.EmployeeId))
            {
                claims.Add(new Claim("employee_id", user.EmployeeId));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            _logger.LogInformation("[SIGNIN] Successfully signed in: {Username}", user.Username);
        }

        public async Task SignOutAsync(HttpContext httpContext)
        {
            _logger.LogInformation("[SIGNOUT] Signing out user");
            
            // Sign out from both cookie and OpenID Connect schemes
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await httpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        }

        public async Task<User?> GetUserByIdAsync(int userId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        }

        public async Task<User?> GetUserByExternalIdAsync(string externalId)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.ExternalId == externalId && u.IsActive);
        }

        private string HashPassword(string password)
        {
            using var sha256 = SHA256.Create();
            var salt = Encoding.UTF8.GetBytes("WorkBot2024Salt"); // Use a proper salt in production
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var saltedPassword = passwordBytes.Concat(salt).ToArray();
            var hash = sha256.ComputeHash(saltedPassword);
            return Convert.ToBase64String(hash);
        }

        private bool VerifyPassword(string password, string hash)
        {
            var computedHash = HashPassword(password);
            return computedHash == hash;
        }
    }
}