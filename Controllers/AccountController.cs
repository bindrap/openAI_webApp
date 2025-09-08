using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using WorkBot.Services;
using WorkBot.Models;
using WorkBot.Data;

namespace WorkBot.Controllers
{
    public class AccountController : Controller
    {
        private readonly IUserService _userService;
        private readonly WorkBotDbContext _context;
        private readonly ILogger<AccountController> _logger;
        private readonly IConfiguration _configuration;

        public AccountController(
            IUserService userService, 
            WorkBotDbContext context, 
            ILogger<AccountController> logger,
            IConfiguration configuration)
        {
            _userService = userService;
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        [HttpGet]
        public IActionResult Login(string? returnUrl = null)
        {
            _logger.LogInformation("[LOGIN_GET] Login page accessed");

            if (User.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("[LOGIN_GET] User already authenticated, redirecting to home");
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            
            // Check if Identity Server is configured
            var identityServerEnabled = !string.IsNullOrEmpty(_configuration["CityWindsor:ClientId"]);
            ViewData["IdentityServerEnabled"] = identityServerEnabled;
            
            return View();
        }

        // Identity Server login
        [HttpGet]
        public IActionResult LoginWithIdentityServer(string? returnUrl = null)
        {
            _logger.LogInformation("[IDENTITY_LOGIN] Starting Identity Server authentication");
            
            var redirectUrl = Url.Action("LoginCallback", "Account", new { returnUrl });
            var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
            
            return Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme);
        }

        // Identity Server callback
        [HttpGet]
        public async Task<IActionResult> LoginCallback(string? returnUrl = null)
        {
            _logger.LogInformation("[IDENTITY_CALLBACK] Processing Identity Server callback");
            
            try
            {
                var result = await HttpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
                
                if (!result.Succeeded)
                {
                    _logger.LogError("[IDENTITY_CALLBACK] Authentication failed");
                    TempData["Error"] = "Authentication failed. Please try again.";
                    return RedirectToAction("Login");
                }

                _logger.LogInformation("[IDENTITY_CALLBACK] Authentication successful, redirecting");
                
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[IDENTITY_CALLBACK] Exception during callback processing");
                TempData["Error"] = "An error occurred during authentication.";
                return RedirectToAction("Login");
            }
        }

        // Legacy local login (keep for fallback)
        [HttpPost]
        public async Task<IActionResult> Login(LoginViewModel model, string? returnUrl = null)
        {
            _logger.LogInformation("[LOGIN_POST] Legacy login attempt for user: {Username}", model?.Username);

            if (model == null)
            {
                _logger.LogWarning("[LOGIN_POST] Model is null");
                ModelState.AddModelError("", "Invalid request");
                return View();
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("[LOGIN_POST] ModelState invalid");
                return View(model);
            }

            try
            {
                var user = await _userService.AuthenticateAsync(model.Username, model.Password);
                if (user == null)
                {
                    _logger.LogWarning("[LOGIN_POST] Authentication failed for user: {Username}", model.Username);
                    ModelState.AddModelError("", "Invalid username or password");
                    return View(model);
                }

                _logger.LogInformation("[LOGIN_POST] Authentication successful for user: {Username}", model.Username);
                await _userService.SignInAsync(HttpContext, user);
                
                if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
                
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOGIN_POST] Exception during login");
                ModelState.AddModelError("", "An error occurred during login: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            _logger.LogInformation("[REGISTER_GET] Register page accessed");

            if (User.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("[REGISTER_GET] User already authenticated, redirecting to home");
                return RedirectToAction("Index", "Home");
            }

            // Check if local registration should be allowed
            var allowLocalRegistration = _configuration.GetValue<bool>("Authentication:AllowLocalRegistration", true);
            if (!allowLocalRegistration)
            {
                TempData["Info"] = "Please use the City of Windsor sign-in to access WorkBot.";
                return RedirectToAction("Login");
            }

            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(RegisterViewModel model)
        {
            _logger.LogInformation("[REGISTER_POST] Register POST called for user: {Username}", model?.Username);

            // Check if local registration is allowed
            var allowLocalRegistration = _configuration.GetValue<bool>("Authentication:AllowLocalRegistration", true);
            if (!allowLocalRegistration)
            {
                TempData["Error"] = "Local registration is disabled. Please use City of Windsor sign-in.";
                return RedirectToAction("Login");
            }

            if (model == null || !ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                var result = await _userService.CreateUserAsync(model.Username, model.Email, model.Password);

                if (!result.Success)
                {
                    _logger.LogWarning("[REGISTER_POST] Registration failed: {Error}", result.ErrorMessage);
                    ModelState.AddModelError("", result.ErrorMessage ?? "Registration failed");
                    return View(model);
                }

                _logger.LogInformation("[REGISTER_POST] User created successfully, redirecting to login");
                TempData["Success"] = "Registration successful! Please log in.";
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[REGISTER_POST] Exception during registration");
                ModelState.AddModelError("", "An error occurred during registration: " + ex.Message);
                return View(model);
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            try
            {
                var username = User.Identity?.Name;
                _logger.LogInformation("[LOGOUT] Logout for user: {Username}", username);

                await _userService.SignOutAsync(HttpContext);
                return RedirectToAction("Login");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOGOUT] Error during logout");
                return RedirectToAction("Login");
            }
        }

        [HttpGet]
        public IActionResult Error()
        {
            ViewData["ErrorMessage"] = TempData["Error"] ?? "An authentication error occurred.";
            return View();
        }

        [HttpGet]
        public IActionResult AccessDenied()
        {
            return View();
        }

        // Debug endpoints (remove in production)
        [HttpGet]
        public IActionResult TestDb()
        {
            try
            {
                _logger.LogInformation("[TEST_DB] Testing database connection...");

                var userCount = _context.Users.Count();
                var users = _context.Users.Take(5).Select(u => new { 
                    u.Id, 
                    u.Username, 
                    u.Email, 
                    u.DisplayName,
                    u.ExternalId,
                    u.EmployeeId,
                    u.AuthenticationMethod,
                    u.CreatedAt 
                }).ToList();

                _logger.LogInformation("[TEST_DB] Database test successful. User count: {UserCount}", userCount);

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
                _logger.LogError(ex, "[TEST_DB] Database test failed");
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