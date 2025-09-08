using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using WorkBot.Services;
using WorkBot.Data;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddHttpClient();

// Configure Entity Framework with SQLite
builder.Services.AddDbContext<WorkBotDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Check if Identity Server is configured
var clientId = builder.Configuration["CityWindsor:ClientId"];
var clientSecret = builder.Configuration["CityWindsor:ClientSecret"];
var identityServerConfigured = !string.IsNullOrEmpty(clientId) && 
                              clientId != "your-client-id-here" && 
                              !string.IsNullOrEmpty(clientSecret) && 
                              clientSecret != "your-client-secret-here";

if (identityServerConfigured)
{
    // Configure Authentication with OpenID Connect (only if properly configured)
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Events.OnSigningOut = async context =>
        {
            // Clear the existing external cookies
            await context.HttpContext.SignOutAsync(OpenIdConnectDefaults.AuthenticationScheme);
        };
    })
    .AddOpenIdConnect(OpenIdConnectDefaults.AuthenticationScheme, options =>
    {
        // Identity Server Configuration
        options.Authority = "https://id-dev.citywindsor.ca";
        options.ClientId = clientId!;
        options.ClientSecret = clientSecret!;
        
        // Set redirect URI explicitly
        options.CallbackPath = "/signin-oidc";
        
        // Required scopes (as specified by senior dev)
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("email");
        // Note: Removed profile, employee_record, offline_access as per senior dev requirements
        
        // Response settings with PKCE as required
        options.ResponseType = OpenIdConnectResponseType.Code;
        options.ResponseMode = OpenIdConnectResponseMode.Query;
        options.UsePkce = true; // PKCE as required by senior dev
        
        // Save tokens to allow for token refresh
        options.SaveTokens = true;
        options.GetClaimsFromUserInfoEndpoint = true;
        
        // Map claims from the Identity Server (limited to what's available with openid + email)
        options.ClaimActions.MapJsonKey(ClaimTypes.NameIdentifier, "sub");
        options.ClaimActions.MapJsonKey(ClaimTypes.Name, "name");
        options.ClaimActions.MapJsonKey(ClaimTypes.Email, "email");
        options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        
        // Handle authentication events
        options.Events = new OpenIdConnectEvents
        {
            OnTokenValidated = async context =>
            {
                var userService = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
                var principal = context.Principal;
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                
                logger.LogInformation("[TOKEN_VALIDATED] Processing token validation");
                
                if (principal?.Identity?.IsAuthenticated == true)
                {
                    // Extract user information from claims
                    var email = principal.FindFirst(ClaimTypes.Email)?.Value;
                    var name = principal.FindFirst(ClaimTypes.Name)?.Value;
                    var givenName = principal.FindFirst(ClaimTypes.GivenName)?.Value;
                    var familyName = principal.FindFirst(ClaimTypes.Surname)?.Value;
                    var preferredUsername = principal.FindFirst("preferred_username")?.Value;
                    var nameIdentifier = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    
                    // Build the best display name from available claims
                    var displayName = DetermineBestDisplayName(name, givenName, familyName, preferredUsername, email);
                    
                    logger.LogInformation("[TOKEN_VALIDATED] Claims - Email: {Email}, Name: {Name}, DisplayName: {DisplayName}, Sub: {Sub}", 
                        email, name, displayName, nameIdentifier);
                    
                    if (!string.IsNullOrEmpty(email) && !string.IsNullOrEmpty(nameIdentifier))
                    {
                        // Create or update user in local database
                        await userService.EnsureUserExistsAsync(nameIdentifier, email, displayName, null);
                        logger.LogInformation("[TOKEN_VALIDATED] User ensured in database");
                    }
                    else
                    {
                        logger.LogWarning("[TOKEN_VALIDATED] Missing required claims - Email: {Email}, Sub: {Sub}", 
                            email, nameIdentifier);
                    }
                }
                
                // Helper method to determine best display name
                static string DetermineBestDisplayName(string? name, string? givenName, string? familyName, string? preferredUsername, string? email)
                {
                    // Priority order:
                    // 1. Full name from 'name' claim
                    // 2. Constructed name from given_name + family_name
                    // 3. Preferred username
                    // 4. Email address
                    
                    if (!string.IsNullOrEmpty(name) && name != "User")
                        return name;
                    
                    if (!string.IsNullOrEmpty(givenName) && !string.IsNullOrEmpty(familyName))
                        return $"{givenName} {familyName}";
                    
                    if (!string.IsNullOrEmpty(givenName))
                        return givenName;
                    
                    if (!string.IsNullOrEmpty(preferredUsername))
                        return preferredUsername;
                    
                    return email ?? "User";
                }
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(context.Exception, "[AUTH_FAILED] OpenID Connect authentication failed");
                
                context.Response.Redirect("/Account/Error");
                context.HandleResponse();
                return Task.CompletedTask;
            },
            OnRemoteFailure = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogError(context.Failure, "[REMOTE_FAILURE] OpenID Connect remote failure");
                
                context.Response.Redirect("/Account/Error");
                context.HandleResponse();
                return Task.CompletedTask;
            },
            OnRedirectToIdentityProvider = context =>
            {
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("[REDIRECT_TO_IDP] Redirecting to Identity Provider: {AuthorityEndpoint}", 
                    context.ProtocolMessage.IssuerAddress);
                return Task.CompletedTask;
            }
        };
        
        // Configure token validation
        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = "https://id-dev.citywindsor.ca";
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = clientId;
    });
}
else
{
    // Fallback to cookie-only authentication if Identity Server not configured
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.LogoutPath = "/Account/Logout";
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
        });
}

// Add services
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IFileProcessingService, FileProcessingService>();
builder.Services.AddScoped<IAzureOpenAIService, AzureOpenAIService>();

// Configure session
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
});

var app = builder.Build();

// Log configuration status
var logger = app.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("Identity Server Configuration Status: {Status}", 
    identityServerConfigured ? "CONFIGURED" : "NOT CONFIGURED - Using fallback authentication");

if (identityServerConfigured)
{
    logger.LogInformation("Identity Server Client ID: {ClientId}", clientId);
    logger.LogInformation("PKCE Enabled: True");
    logger.LogInformation("Scopes: openid, email");
}

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// Ensure database is created
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<WorkBotDbContext>();
    context.Database.EnsureCreated();
}

// Configure routing
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();