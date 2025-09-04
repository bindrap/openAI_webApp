# WorkBot .NET Conversion - Deployment Guide

## Overview
This is a complete conversion of the Python Flask WorkBot application to .NET 8 with ASP.NET Core MVC, designed for enterprise deployment on City of Windsor servers.

## Key Benefits for Municipal Deployment

### 🏢 Enterprise Features
- **Windows Server Integration**: Native Windows Server compatibility
- **IIS Hosting**: Seamless integration with existing IIS infrastructure
- **Active Directory**: Easy integration with existing AD authentication
- **SQL Server Support**: Can easily switch from SQLite to SQL Server
- **Enterprise Security**: Built-in security features for government use

### 🔒 Security Enhancements
- **Authentication Cookies**: Secure session management
- **CSRF Protection**: Built-in request validation
- **Input Validation**: Automatic model validation
- **SQL Injection Prevention**: Entity Framework protection
- **XSS Protection**: Razor view engine security

## Project Structure
```
WorkBot/
├── Program.cs                  # Application startup
├── WorkBot.csproj             # Project configuration
├── appsettings.json           # Configuration
├── Controllers/
│   ├── HomeController.cs      # Main application controller
│   ├── AccountController.cs   # Authentication
│   └── ApiController.cs       # API endpoints
├── Data/
│   └── WorkBotDbContext.cs    # Database models
├── Services/
│   ├── UserService.cs         # User management
│   ├── ConversationService.cs # Chat functionality
│   ├── FileProcessingService.cs # File handling
│   └── AzureOpenAIService.cs  # AI integration
├── Models/
│   └── ViewModels.cs          # Request/response models
├── Views/
│   ├── Home/
│   │   └── Index.cshtml       # Main chat interface
│   └── Account/
│       ├── Login.cshtml       # Login page
│       └── Register.cshtml    # Registration page
└── wwwroot/
    ├── css/
    ├── js/
    └── uploads/               # File storage
```

## Installation Steps

### 1. Prerequisites
```bash
# Install .NET 8 SDK
# Download from: https://dotnet.microsoft.com/download/dotnet/8.0

# Verify installation
dotnet --version
```

### 2. Create Project
```bash
# Create new directory
mkdir WorkBot
cd WorkBot

# Create new MVC project
dotnet new mvc --name WorkBot

# Add required packages
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.AspNetCore.Authentication.Cookies
dotnet add package System.Text.Json

# Optional: For full file processing
dotnet add package itext7
dotnet add package DocumentFormat.OpenXml
dotnet add package Azure.AI.FormRecognizer
```

### 3. Configuration

#### Update appsettings.json
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=workbot.db"
  },
  "AzureOpenAI": {
    "Endpoint": "YOUR_AZURE_ENDPOINT",
    "ApiKey": "YOUR_API_KEY",
    "DeploymentName": "gpt-5-mini",
    "ApiVersion": "2024-12-01-preview"
  }
}
```

#### For SQL Server (Production)
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=SERVER_NAME;Database=WorkBot;Trusted_Connection=true;TrustServerCertificate=true;"
  }
}
```

### 4. Database Setup
```bash
# Create initial migration
dotnet ef migrations add InitialCreate

# Update database
dotnet ef database update
```

### 5. Run Application
```bash
# Development
dotnet run

# Production build
dotnet publish -c Release -o publish
```

## IIS Deployment (Windows Server)

### 1. Install Prerequisites
- .NET 8 Hosting Bundle
- IIS with ASP.NET Core Module

### 2. Deploy to IIS
```bash
# Publish application
dotnet publish -c Release -o C:\inetpub\workbot

# Create IIS Application
# - Point to C:\inetpub\workbot
# - Set Application Pool to "No Managed Code"
# - Enable Windows Authentication if using AD
```

### 3. Configure for City Network
```xml
<!-- web.config -->
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>
    <handlers>
      <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
    </handlers>
    <aspNetCore processPath="dotnet" arguments=".\WorkBot.dll" stdoutLogEnabled="false" stdoutLogFile=".\logs\stdout" />
  </system.webServer>
</configuration>
```

## Active Directory Integration

### Add to Program.cs:
```csharp
builder.Services.AddAuthentication(IISDefaults.AuthenticationScheme)
    .AddNegotiate(); // Windows Authentication

// Or for LDAP integration:
builder.Services.AddAuthentication()
    .AddLdap(options =>
    {
        options.Server = "ldap://your-domain-controller";
        options.Port = 389;
        options.SearchBase = "DC=cityofwindsor,DC=ca";
    });
```

## Database Migration to SQL Server

### Update Connection String:
```csharp
// In Program.cs
builder.Services.AddDbContext<WorkBotDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));
```

### Migration Commands:
```bash
# Remove SQLite migrations
rm -rf Migrations/

# Add SQL Server migration
dotnet ef migrations add InitialSqlServer

# Update database
dotnet ef database update
```

## Performance Optimizations

### 1. Caching
```csharp
// Add to Program.cs
builder.Services.AddMemoryCache();
builder.Services.AddResponseCaching();
```

### 2. Connection Pooling
```csharp
builder.Services.AddDbContextPool<WorkBotDbContext>(options =>
    options.UseSqlServer(connectionString));
```

### 3. Health Checks
```csharp
builder.Services.AddHealthChecks()
    .AddDbContext<WorkBotDbContext>()
    .AddCheck("azure-openai", () => HealthCheckResult.Healthy());

app.MapHealthChecks("/health");
```

## Security Considerations

### 1. HTTPS Enforcement
```csharp
app.UseHttpsRedirection();
app.UseHsts();
```

### 2. Security Headers
```csharp
app.Use(async (context, next) =>
{
    context.Response.Headers.Add("X-Frame-Options", "DENY");
    context.Response.Headers.Add("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Add("Referrer-Policy", "strict-origin-when-cross-origin");
    await next();
});
```

### 3. File Upload Security
```csharp
// In FileProcessingService
private readonly string[] _allowedMimeTypes = { 
    "text/plain", "application/pdf", "image/jpeg", "image/png" 
};

private bool IsValidFile(IFormFile file)
{
    return _allowedMimeTypes.Contains(file.ContentType) &&
           file.Length < 10_000_000; // 10MB limit
}
```

## Monitoring & Logging

### 1. Application Insights
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

### 2. Structured Logging
```csharp
builder.Services.AddLogging(builder =>
{
    builder.AddConsole();
    builder.AddEventLog();
    builder.AddApplicationInsights();
});
```

## Load Balancing Setup

### For multiple servers:
```csharp
// Add to Program.cs
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<WorkBotDbContext>()
    .SetApplicationName("WorkBot");

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
```

## Key Advantages of .NET Version

1. **Better Performance**: Compiled code vs interpreted Python
2. **Enterprise Integration**: Native Windows/AD support
3. **Type Safety**: Compile-time error checking
4. **Scalability**: Better memory management and threading
5. **Maintenance**: Strong tooling and IDE support
6. **Security**: Built-in security features
7. **Deployment**: Easy IIS integration

## Migration Checklist

- ✅ User authentication and sessions
- ✅ File upload and processing
- ✅ Database operations with Entity Framework
- ✅ Azure OpenAI integration
- ✅ Real-time chat functionality
- ✅ Multi-file support
- ✅ Conversation management
- ⚠️  OCR integration (requires Azure Cognitive Services)
- ⚠️  Advanced file processing (PDF, DOCX libraries)
- ⚠️  Memory consolidation features

The converted .NET application maintains all core functionality while providing enterprise-grade security, performance, and integration capabilities suitable for municipal government deployment.