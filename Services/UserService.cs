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
            _logger.LogInformation("[IDENTITY] Ensuring user exists - ExternalId: {ExternalId}, Email: {Email}, DisplayName: {DisplayName}", 
                externalId, email, displayName);
            
            // Try to find user by external ID first, then by email
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.ExternalId == externalId || u.Email == email);

            if (user == null)
            {
                // Create new user
                _logger.LogInformation("[IDENTITY] Creating new user for ExternalId: {ExternalId}", externalId);
                
                // Determine the best display name and username
                var bestDisplayName = DetermineBestDisplayName(displayName, email);
                var username = DetermineUsername(email, displayName);
                
                user = new User
                {
                    ExternalId = externalId,
                    Username = username,
                    Email = email,
                    DisplayName = bestDisplayName,
                    EmployeeId = employeeId,
                    PasswordHash = string.Empty, // No password needed for SSO users
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true,
                    AuthenticationMethod = "IdentityServer"
                };

                _context.Users.Add(user);
                _logger.LogInformation("[IDENTITY] New user created - Username: {Username}, DisplayName: {DisplayName}", 
                    username, bestDisplayName);
            }
            else
            {
                // Update existing user
                _logger.LogInformation("[IDENTITY] Updating existing user: {UserId}", user.Id);
                
                user.ExternalId = externalId;
                user.Email = email;
                
                // Update display name if we have better information
                var newDisplayName = DetermineBestDisplayName(displayName, email);
                if (!string.IsNullOrEmpty(newDisplayName) && 
                    (string.IsNullOrEmpty(user.DisplayName) || user.DisplayName == "User" || user.DisplayName == user.Username))
                {
                    user.DisplayName = newDisplayName;
                    _logger.LogInformation("[IDENTITY] Updated display name to: {DisplayName}", newDisplayName);
                }
                
                user.EmployeeId = employeeId ?? user.EmployeeId;
                user.AuthenticationMethod = "IdentityServer";
            }

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("[IDENTITY] User ensured successfully: {UserId}, DisplayName: {DisplayName}", 
                user.Id, user.DisplayName);
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
                    DisplayName = username, // Use username as display name for local accounts
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
            _logger.LogInformation("[SIGNIN] Signing in user: {Username}, DisplayName: {DisplayName}", 
                user.Username, user.DisplayName);
            
            // Use the best available name for display
            var displayName = user.DisplayName ?? user.Username;
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, displayName), // This is what shows up in User.Identity.Name
                new Claim(ClaimTypes.Email, user.Email),
                new Claim("username", user.Username),
                new Claim("display_name", displayName)
            };

            if (!string.IsNullOrEmpty(user.EmployeeId))
            {
                claims.Add(new Claim("employee_id", user.EmployeeId));
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            _logger.LogInformation("[SIGNIN] Successfully signed in: {Username} as {DisplayName}", user.Username, displayName);
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

        private string DetermineBestDisplayName(string? displayName, string email)
        {
            // Priority order for display name:
            // 1. Use provided display name if it exists and isn't generic
            // 2. Use email address
            
            if (!string.IsNullOrEmpty(displayName) && 
                displayName != "User" && 
                displayName.Length > 2)
            {
                return displayName;
            }
            
            // Fall back to email address (it's better than "User")
            return email;
        }

        private string DetermineUsername(string email, string? displayName)
        {
            // For username, prefer the email prefix, but make it unique if needed
            var baseUsername = email.Split('@')[0];
            
            // Clean up the username (remove dots, etc.)
            baseUsername = baseUsername.Replace(".", "").Replace("-", "").Replace("_", "");
            
            return baseUsername;
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