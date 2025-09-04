using WorkBot.Models;

namespace WorkBot.Services
{
    public interface IFileProcessingService
    {
        Task<List<SessionFileDto>> SaveSessionFilesAsync(string sessionId, List<IFormFile> files);
        Task<List<SessionFileDto>> GetSessionFilesAsync(string sessionId);
        Task RemoveSessionFileAsync(string sessionId, int fileId);
        Task ClearSessionFilesAsync(string sessionId);
    }
}