using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.Communication;

public class NoticeCategory : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class Notice : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    public long? NoticeCategoryId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [Required] public string Body { get; set; } = string.Empty;
    public DateTime PublishAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsPublished { get; set; }
}

public class Notification : BaseTenantEntity
{
    public long UserId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [Required, MaxLength(2000)] public string Body { get; set; } = string.Empty;
    [MaxLength(100)] public string? TypeCode { get; set; }
    [MaxLength(100)] public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    [MaxLength(500)] public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class NotificationPreference : BaseTenantEntity
{
    public long UserId { get; set; }
    public NotificationChannelType Channel { get; set; }
    [Required, MaxLength(100)] public string EventCode { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

public class MessageThread : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [MaxLength(250)] public string? Subject { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;
}

public class Message : BaseTenantEntity
{
    public long MessageThreadId { get; set; }
    public long SenderUserId { get; set; }
    public long RecipientUserId { get; set; }
    [Required, MaxLength(4000)] public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}

public class MessageTemplate : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    [MaxLength(250)] public string? SubjectTemplate { get; set; }
    [Required] public string BodyTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class DeviceToken : BaseTenantEntity
{
    public long UserId { get; set; }
    [Required, MaxLength(1000)] public string Token { get; set; } = string.Empty;
    [MaxLength(50)] public string? Platform { get; set; }
    [MaxLength(100)] public string? DeviceId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}

public class CommunicationGateway : BaseTenantEntity
{
    [Required, MaxLength(100)] public string ProviderCode { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    [MaxLength(1000)] public string? Endpoint { get; set; }
    [MaxLength(4000)] public string? ProtectedCredential { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CommunicationDelivery : BaseTenantEntity
{
    public Guid IdempotencyKey { get; set; }
    public NotificationChannelType Channel { get; set; }
    [Required, MaxLength(300)] public string Destination { get; set; } = string.Empty;
    [MaxLength(250)] public string? Subject { get; set; }
    [Required] public string Body { get; set; } = string.Empty;
    public DeliveryState State { get; set; } = DeliveryState.Pending;
    public long? CommunicationGatewayId { get; set; }
    public long? UserId { get; set; }
    [MaxLength(150)] public string? ProviderMessageId { get; set; }
    [MaxLength(100)] public string? SourceType { get; set; }
    public long? SourceId { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    [MaxLength(1000)] public string? FailureReason { get; set; }
}

public class NoticeAudience : BaseTenantEntity
{
    public long NoticeId { get; set; }
    [Required, MaxLength(50)] public string AudienceTypeCode { get; set; } = string.Empty;
    public long? CampusId { get; set; }
    public long? AcademicProgramId { get; set; }
    public long? AcademicBatchId { get; set; }
    public long? UserId { get; set; }
}

public class NoticeReadReceipt : BaseTenantEntity
{
    public long NoticeId { get; set; }
    public long UserId { get; set; }
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

public class MessageThreadParticipant : BaseTenantEntity
{
    public long MessageThreadId { get; set; }
    public long UserId { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
    public DateTime? LastReadAt { get; set; }
}

public class MessageAttachment : BaseTenantEntity
{
    public long MessageId { get; set; }
    public long FileAssetId { get; set; }
}
