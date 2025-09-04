# PowerShell script to properly organize your WorkBot project

Write-Host "Setting up WorkBot project structure..." -ForegroundColor Green

# 1. Move ViewModels.cs to correct location
if (Test-Path "Views/ViewModels.cs") {
    Write-Host "Moving ViewModels.cs to Models folder..." -ForegroundColor Yellow
    Move-Item "Views/ViewModels.cs" "Models/" -Force
}

# 2. Delete the incorrect combined files
if (Test-Path "Controllers/controllers.cs") {
    Write-Host "Removing combined controllers.cs file..." -ForegroundColor Yellow
    Remove-Item "Controllers/controllers.cs" -Force
}

if (Test-Path "Services/services.cs") {
    Write-Host "Removing combined services.cs file..." -ForegroundColor Yellow
    Remove-Item "Services/services.cs" -Force
}

# 3. Create missing folders in wwwroot
$wwwrootFolders = @("css", "js", "lib", "uploads")
foreach ($folder in $wwwrootFolders) {
    $path = "wwwroot/$folder"
    if (-not (Test-Path $path)) {
        Write-Host "Creating folder: $path" -ForegroundColor Green
        New-Item -ItemType Directory -Path $path -Force
    }
}

# 4. Create a basic site.css file
$siteCSS = @"
/* WorkBot Site Styles */
body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
}

.container {
    max-width: 1200px;
    margin: 0 auto;
    padding: 20px;
}
"@

$siteCSS | Out-File "wwwroot/css/site.css" -Encoding UTF8

# 5. Create a basic site.js file
$siteJS = @"
// WorkBot Site JavaScript
console.log('WorkBot loaded successfully');
"@

$siteJS | Out-File "wwwroot/js/site.js" -Encoding UTF8

# 6. Create missing DTO file
$dtoContent = @"
namespace WorkBot.Models
{
    public class CreateUserResult
    {
        public bool Success { get; set; }
        public int UserId { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class MessageDto
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public bool HasFiles { get; set; }
    }

    public class SessionFileDto
    {
        public int Id { get; set; }
        public string OriginalFilename { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ExtractedText { get; set; } = string.Empty;
    }

    public class ConversationDto
    {
        public string Id { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int MessageCount { get; set; }
    }
}
"@

$dtoContent | Out-File "Models/DTOs.cs" -Encoding UTF8

Write-Host "Project structure setup complete!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Copy the controller files I provided into Controllers/" -ForegroundColor White
Write-Host "2. Copy the service files I provided into Services/" -ForegroundColor White
Write-Host "3. Update your appsettings.json with Azure OpenAI keys" -ForegroundColor White
Write-Host "4. Run 'dotnet build' to check for errors" -ForegroundColor White