using Microsoft.AspNetCore.Mvc;
using WorkBot.Services;
using WorkBot.Models;
using WorkBot.Data;

namespace WorkBot.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserService _userService;
        private readonly WorkBotDbContext _context;

        public AccountController(IUserService userService, WorkBotDbContext context)
        {
            _userService = userService;
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            Console.WriteLine("[LOGIN_GET] Login page accessed");

            if (User.Identity?.IsAuthenticated == true)
            {
                Console.WriteLine("[LOGIN_GET] User already authenticated, redirecting to home");
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            Console.WriteLine($"[LOGIN_POST] Login attempt for user: {model?.Username}");
            Console.WriteLine($"[LOGIN_POST] ModelState.IsValid: {ModelState.IsValid}");

            if (model == null)
            {
                Console.WriteLine("[LOGIN_POST] Model is null!");
                ModelState.AddModelError("", "Invalid request");
                return View();
            }

            if (!ModelState.IsValid)
            {
                Console.WriteLine("[LOGIN_POST] ModelState invalid:");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    Console.WriteLine($"[LOGIN_POST] Validation error: {error.ErrorMessage}");
                }
                return View(model);
            }

            try
            {
                var user = await _userService.AuthenticateAsync(model.Username, model.Password);
                if (user == null)
                {
                    Console.WriteLine($"[LOGIN_POST] Authentication failed for user: {model.Username}");
                    ModelState.AddModelError("", "Invalid username or password");
                    return View(model);
                }

                Console.WriteLine($"[LOGIN_POST] Authentication successful for user: {model.Username}");
                await _userService.SignInAsync(HttpContext, user);
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGIN_POST] Exception during login: {ex.Message}");
                Console.WriteLine($"[LOGIN_POST] Stack trace: {ex.StackTrace}");
                ModelState.AddModelError("", "An error occurred during login: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            Console.WriteLine("[REGISTER_GET] Register page accessed");

            if (User.Identity?.IsAuthenticated == true)
            {
                Console.WriteLine("[REGISTER_GET] User already authenticated, redirecting to home");
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            Console.WriteLine($"[REGISTER_POST] Register POST called for user: {model?.Username}");
            Console.WriteLine($"[REGISTER_POST] ModelState.IsValid: {ModelState.IsValid}");

            if (model == null)
            {
                Console.WriteLine("[REGISTER_POST] Model is null!");
                ModelState.AddModelError("", "Invalid request");
                return View();
            }

            // Log all model properties
            Console.WriteLine($"[REGISTER_POST] Model data - Username: '{model.Username}', Email: '{model.Email}', Password length: {model.Password?.Length ?? 0}, Confirm length: {model.ConfirmPassword?.Length ?? 0}");

            if (!ModelState.IsValid)
            {
                Console.WriteLine("[REGISTER_POST] ModelState invalid:");
                foreach (var kvp in ModelState)
                {
                    foreach (var error in kvp.Value.Errors)
                    {
                        Console.WriteLine($"[REGISTER_POST] Validation error for {kvp.Key}: {error.ErrorMessage}");
                    }
                }
                return View(model);
            }

            Console.WriteLine($"[REGISTER_POST] Attempting to create user: {model.Username}, {model.Email}");

            try
            {
                var result = await _userService.CreateUserAsync(model.Username, model.Email, model.Password);
                Console.WriteLine($"[REGISTER_POST] CreateUserAsync result - Success: {result.Success}, Error: {result.ErrorMessage}");

                if (!result.Success)
                {
                    Console.WriteLine($"[REGISTER_POST] Registration failed: {result.ErrorMessage}");
                    ModelState.AddModelError("", result.ErrorMessage ?? "Registration failed");
                    return View(model);
                }

                Console.WriteLine($"[REGISTER_POST] User created successfully, redirecting to login");
                TempData["Success"] = "Registration successful! Please log in.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[REGISTER_POST] Exception: {ex.Message}");
                Console.WriteLine($"[REGISTER_POST] Stack trace: {ex.StackTrace}");
                ModelState.AddModelError("", "An error occurred during registration: " + ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var username = User.Identity?.Name;
                Console.WriteLine($"[LOGOUT] Logout for user: {username}");

                await _userService.SignOutAsync(HttpContext);
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[LOGOUT] Error during logout: {ex.Message}");
                return RedirectToAction("Login");
            }
        }

        // Debug endpoint - remove after fixing
        [HttpGet]
        public IActionResult TestDb()
        {
            try
            {
                Console.WriteLine("[TEST_DB] Testing database connection...");

                var userCount = _context.Users.Count();
                var users = _context.Users.Take(5).Select(u => new { u.Id, u.Username, u.Email, u.CreatedAt }).ToList();

                Console.WriteLine($"[TEST_DB] Database test successful. User count: {userCount}");

                return Json(new
                {
                    success = true,
                    userCount = userCount,
                    users = users,
                    message = "Database connection successful"
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST_DB] Database test failed: {ex.Message}");
                Console.WriteLine($"[TEST_DB] Stack trace: {ex.StackTrace}");

                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }

        // Debug registration endpoint - remove after fixing
        [HttpGet]
        public async Task<IActionResult> TestRegister()
        {
            try
            {
                Console.WriteLine("[TEST_REG] Testing registration process...");

                var testUsername = "testuser_" + DateTime.Now.Ticks;
                var testEmail = $"test_{DateTime.Now.Ticks}@test.com";
                var testPassword = "password123";

                var result = await _userService.CreateUserAsync(testUsername, testEmail, testPassword);

                return Json(new
                {
                    success = result.Success,
                    userId = result.UserId,
                    error = result.ErrorMessage,
                    testData = new { username = testUsername, email = testEmail, password = "***" }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[TEST_REG] Test registration failed: {ex.Message}");
                return Json(new
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }
        
        // Add this debug action to your AccountController to test if ViewModels are working

        [HttpGet]
        public IActionResult TestViewModels()
        {
            try
            {
                // Test creating ViewModels
                var loginModel = new LoginViewModel 
                { 
                    Username = "test", 
                    Password = "test" 
                };
                
                var registerModel = new RegisterViewModel 
                { 
                    Username = "test", 
                    Email = "test@test.com", 
                    Password = "test123", 
                    ConfirmPassword = "test123" 
                };

                return Json(new 
                {
                    success = true,
                    loginModel = new { loginModel.Username, PasswordSet = !string.IsNullOrEmpty(loginModel.Password) },
                    registerModel = new { 
                        registerModel.Username, 
                        registerModel.Email, 
                        PasswordSet = !string.IsNullOrEmpty(registerModel.Password),
                        ConfirmPasswordSet = !string.IsNullOrEmpty(registerModel.ConfirmPassword)
                    },
                    message = "ViewModels working correctly"
                });
            }
            catch (Exception ex)
            {
                return Json(new 
                {
                    success = false,
                    error = ex.Message,
                    stackTrace = ex.StackTrace
                });
            }
        }
    }
}