// Services/UserService.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

        public UserService(WorkBotDbContext context)
        {
            _context = context;
        }

        public async Task<User?> AuthenticateAsync(string username, string password)
        {
            Console.WriteLine($"[AUTH] Authenticating user: {username}");
            
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

            if (user == null)
            {
                Console.WriteLine($"[AUTH] User not found: {username}");
                return null;
            }

            var isValidPassword = VerifyPassword(password, user.PasswordHash);
            Console.WriteLine($"[AUTH] Password valid: {isValidPassword}");

            if (!isValidPassword)
                return null;

            user.LastLogin = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            Console.WriteLine($"[AUTH] Authentication successful for: {username}");
            return user;
        }

        public async Task<CreateUserResult> CreateUserAsync(string username, string email, string password)
        {
            try
            {
                Console.WriteLine($"[CREATE_USER] Starting user creation for: {username}, {email}");
                
                // Check if username or email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

                if (existingUser != null)
                {
                    var field = existingUser.Username == username ? "Username" : "Email";
                    Console.WriteLine($"[CREATE_USER] User already exists - {field}: {existingUser.Username} / {existingUser.Email}");
                    return new CreateUserResult { Success = false, ErrorMessage = $"{field} already exists" };
                }

                Console.WriteLine($"[CREATE_USER] No existing user found, creating new user...");

                var hashedPassword = HashPassword(password);
                Console.WriteLine($"[CREATE_USER] Password hashed successfully");

                var user = new User
                {
                    Username = username,
                    Email = email,
                    PasswordHash = hashedPassword,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                Console.WriteLine($"[CREATE_USER] User object created, adding to context...");
                
                _context.Users.Add(user);
                
                Console.WriteLine($"[CREATE_USER] Saving changes to database...");
                var result = await _context.SaveChangesAsync();
                
                Console.WriteLine($"[CREATE_USER] SaveChanges result: {result} rows affected");
                Console.WriteLine($"[CREATE_USER] User created successfully with ID: {user.Id}");

                return new CreateUserResult { Success = true, UserId = user.Id };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[CREATE_USER] Exception in CreateUser: {ex.Message}");
                Console.WriteLine($"[CREATE_USER] Stack trace: {ex.StackTrace}");
                return new CreateUserResult { Success = false, ErrorMessage = ex.Message };
            }
        }

        public async Task SignInAsync(HttpContext httpContext, User user)
        {
            Console.WriteLine($"[SIGNIN] Signing in user: {user.Username}");
            
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim(ClaimTypes.Email, user.Email)
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await httpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
            Console.WriteLine($"[SIGNIN] Successfully signed in: {user.Username}");
        }

        public async Task SignOutAsync(HttpContext httpContext)
        {
            Console.WriteLine($"[SIGNOUT] Signing out user");
            await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }

        private string HashPassword(string password)
        {
            Console.WriteLine($"[HASH] Hashing password (length: {password.Length})");
            using var sha256 = SHA256.Create();
            var salt = Encoding.UTF8.GetBytes("WorkBot2024Salt"); // Use a proper salt in production
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var saltedPassword = passwordBytes.Concat(salt).ToArray();
            var hash = sha256.ComputeHash(saltedPassword);
            var result = Convert.ToBase64String(hash);
            Console.WriteLine($"[HASH] Password hashed successfully");
            return result;
        }

        private bool VerifyPassword(string password, string hash)
        {
            var computedHash = HashPassword(password);
            var isMatch = computedHash == hash;
            Console.WriteLine($"[VERIFY] Password verification result: {isMatch}");
            return isMatch;
        }

        // Debug method - remove after fixing
        public async Task<string> DebugCreateUserAsync(string username, string email, string password)
        {
            try
            {
                Console.WriteLine($"[DEBUG] Starting user creation for: {username}, {email}");
                
                // Check if username or email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Username == username || u.Email == email);

                if (existingUser != null)
                {
                    var field = existingUser.Username == username ? "Username" : "Email";
                    Console.WriteLine($"[DEBUG] User already exists - {field}: {existingUser.Username} / {existingUser.Email}");
                    return $"Error: {field} already exists";
                }

                Console.WriteLine($"[DEBUG] No existing user found, creating new user...");

                var hashedPassword = HashPassword(password);
                Console.WriteLine($"[DEBUG] Password hashed successfully");

                var user = new User
                {
                    Username = username,
                    Email = email,
                    PasswordHash = hashedPassword,
                    CreatedAt = DateTime.UtcNow,
                    IsActive = true
                };

                Console.WriteLine($"[DEBUG] User object created, adding to context...");
                
                _context.Users.Add(user);
                
                Console.WriteLine($"[DEBUG] Saving changes to database...");
                var result = await _context.SaveChangesAsync();
                
                Console.WriteLine($"[DEBUG] SaveChanges result: {result} rows affected");
                Console.WriteLine($"[DEBUG] User created successfully with ID: {user.Id}");

                return $"Success: User created with ID {user.Id}";
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[DEBUG] Exception in CreateUser: {ex.Message}");
                Console.WriteLine($"[DEBUG] Stack trace: {ex.StackTrace}");
                return $"Error: {ex.Message}";
            }
        }
    }
}