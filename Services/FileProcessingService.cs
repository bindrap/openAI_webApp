using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using WorkBot.Data;
using WorkBot.Models;

namespace WorkBot.Services
{
    public class FileProcessingService : IFileProcessingService
    {
        private readonly WorkBotDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly string _uploadPath;
        private readonly HashSet<string> _allowedExtensions = new() { ".txt", ".pdf", ".docx", ".png", ".jpg", ".jpeg", ".webp", ".csv", ".json" };

        public FileProcessingService(WorkBotDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
            _uploadPath = Path.Combine(_environment.ContentRootPath, "wwwroot", "uploads");
            Directory.CreateDirectory(_uploadPath);
        }

        public async Task<List<SessionFileDto>> SaveSessionFilesAsync(string sessionId, List<IFormFile> files)
        {
            var result = new List<SessionFileDto>();

            foreach (var file in files)
            {
                if (file.Length == 0) continue;

                var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
                if (!_allowedExtensions.Contains(extension)) continue;

                var storedFilename = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
                var filePath = Path.Combine(_uploadPath, storedFilename);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var extractedText = await ExtractTextFromFileAsync(filePath);
                var fileHash = await ComputeFileHashAsync(filePath);

                var sessionFile = new SessionFile
                {
                    SessionId = sessionId,
                    OriginalFilename = file.FileName,
                    StoredFilename = storedFilename,
                    FileHash = fileHash,
                    FileSize = file.Length,
                    MimeType = file.ContentType ?? "application/octet-stream",
                    ExtractedText = extractedText,
                    UploadTime = DateTime.UtcNow,
                    IsActive = true
                };

                _context.SessionFiles.Add(sessionFile);
                await _context.SaveChangesAsync();

                result.Add(new SessionFileDto
                {
                    Id = sessionFile.Id,
                    OriginalFilename = sessionFile.OriginalFilename,
                    FileSize = sessionFile.FileSize,
                    ExtractedText = sessionFile.ExtractedText
                });
            }

            return result;
        }

        public async Task<List<SessionFileDto>> GetSessionFilesAsync(string sessionId)
        {
            var files = await _context.SessionFiles
                .Where(f => f.SessionId == sessionId && f.IsActive)
                .OrderByDescending(f => f.UploadTime)
                .ToListAsync();

            return files.Select(f => new SessionFileDto
            {
                Id = f.Id,
                OriginalFilename = f.OriginalFilename,
                FileSize = f.FileSize,
                ExtractedText = f.ExtractedText
            }).ToList();
        }

        public async Task RemoveSessionFileAsync(string sessionId, int fileId)
        {
            var file = await _context.SessionFiles
                .FirstOrDefaultAsync(f => f.Id == fileId && f.SessionId == sessionId);

            if (file != null)
            {
                // Delete physical file
                var filePath = Path.Combine(_uploadPath, file.StoredFilename);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Mark as inactive
                file.IsActive = false;
                await _context.SaveChangesAsync();
            }
        }

        public async Task ClearSessionFilesAsync(string sessionId)
        {
            var files = await _context.SessionFiles
                .Where(f => f.SessionId == sessionId && f.IsActive)
                .ToListAsync();

            foreach (var file in files)
            {
                // Delete physical file
                var filePath = Path.Combine(_uploadPath, file.StoredFilename);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                // Mark as inactive
                file.IsActive = false;
            }

            await _context.SaveChangesAsync();
        }

        private async Task<string> ExtractTextFromFileAsync(string filePath)
        {
            try
            {
                var extension = Path.GetExtension(filePath).ToLowerInvariant();
                
                return extension switch
                {
                    ".txt" => await File.ReadAllTextAsync(filePath),
                    ".json" => await File.ReadAllTextAsync(filePath),
                    ".csv" => await ExtractTextFromCsvAsync(filePath),
                    ".pdf" => "[PDF text extraction - requires PDF library implementation]",
                    ".docx" => "[DOCX text extraction - requires OpenXML library implementation]",
                    ".png" or ".jpg" or ".jpeg" or ".webp" => "[OCR text extraction - requires Azure Cognitive Services implementation]",
                    _ => "[UNSUPPORTED FILE TYPE]"
                };
            }
            catch (Exception ex)
            {
                return $"[ERROR: Could not extract text] {ex.Message}";
            }
        }

        private async Task<string> ExtractTextFromCsvAsync(string filePath)
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            return string.Join("\n", lines);
        }

        private async Task<string> ComputeFileHashAsync(string filePath)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(filePath);
            var hash = await md5.ComputeHashAsync(stream);
            return Convert.ToHexString(hash);
        }
    }
}