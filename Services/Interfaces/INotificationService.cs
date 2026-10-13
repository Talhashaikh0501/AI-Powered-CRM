using System.Collections.Generic;
using System.Threading.Tasks;
using AI_Powered_CRM.Models.Entities;

namespace AI_Powered_CRM.Services.Interfaces
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(string userId, string title, string message, int? leadId = null);
        Task<List<Notification>> GetUserNotificationsAsync(string userId, int count = 20, bool unreadOnly = false);
        Task<int> GetUnreadCountAsync(string userId);
        Task<bool> MarkAsReadAsync(int notificationId, string userId);
        Task MarkAllAsReadAsync(string userId);
    }
}
