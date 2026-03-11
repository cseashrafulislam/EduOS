using System;
using EduOS.Core.Entities.Common;

namespace EduOS.Core.Entities.AI
{
    public class ChatbotConversation : TenantEntity
    {
        public int? StudentId { get; set; }
        public string UserMessage { get; set; }
        public string BotReply { get; set; }
        public DateTime MessageTime { get; set; } = DateTime.UtcNow;
    }
}
