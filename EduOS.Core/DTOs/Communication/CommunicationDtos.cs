namespace EduOS.Core.DTOs.Communication;

public class NoticeCategoryDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveNoticeCategoryRequestDto
{
    [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(50)] public string Code { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class NoticeDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public long? NoticeCategoryId { get; set; }
    public string? NoticeCategoryName { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime PublishAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsPublished { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveNoticeRequestDto
{
    public long? NoticeCategoryId { get; set; }
    [Required, MaxLength(250)] public string Title { get; set; } = string.Empty;
    [Required] public string Body { get; set; } = string.Empty;
    public DateTime PublishAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public bool IsPublished { get; set; }
    public string? RowVersion { get; set; }
}

public class NotificationDto
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? TypeCode { get; set; }
    public string? ActionUrl { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class NotificationPreferenceDto
{
    public long Id { get; set; }
    public NotificationChannelType Channel { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveNotificationPreferenceRequestDto
{
    public NotificationChannelType Channel { get; set; }
    [Required, MaxLength(100)] public string EventCode { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class MessageThreadDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string? Subject { get; set; }
    public DateTime LastMessageAt { get; set; }
    public IReadOnlyList<MessageDto> Messages { get; set; } = Array.Empty<MessageDto>();
}

public class MessageDto
{
    public long Id { get; set; }
    public long SenderUserId { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public long RecipientUserId { get; set; }
    public string RecipientName { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; }
    public DateTime? ReadAt { get; set; }
}

public class SendMessageRequestDto
{
    public Guid? ThreadReference { get; set; }
    public long RecipientUserId { get; set; }
    [MaxLength(250)] public string? Subject { get; set; }
    [Required, MaxLength(4000)] public string Body { get; set; } = string.Empty;
}

public class MessageTemplateDto
{
    public long Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    public string? SubjectTemplate { get; set; }
    public string BodyTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveMessageTemplateRequestDto
{
    [Required, MaxLength(100)] public string Code { get; set; } = string.Empty;
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    [MaxLength(250)] public string? SubjectTemplate { get; set; }
    [Required] public string BodyTemplate { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class DeviceTokenDto
{
    public long Id { get; set; }
    public string? Platform { get; set; }
    public string? DeviceId { get; set; }
    public bool IsActive { get; set; }
    public DateTime LastSeenAt { get; set; }
}

public class RegisterDeviceTokenRequestDto
{
    [Required, MaxLength(1000)] public string Token { get; set; } = string.Empty;
    [MaxLength(50)] public string? Platform { get; set; }
    [MaxLength(100)] public string? DeviceId { get; set; }
}

public class CommunicationGatewayDto
{
    public long Id { get; set; }
    public string ProviderCode { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    public string? Endpoint { get; set; }
    public bool HasCredential { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCommunicationGatewayRequestDto
{
    [Required, MaxLength(100)] public string ProviderCode { get; set; } = string.Empty;
    public NotificationChannelType Channel { get; set; }
    [MaxLength(1000)] public string? Endpoint { get; set; }
    public string? Credential { get; set; }
    public bool ClearCredential { get; set; }
    public bool IsDefault { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class CommunicationDeliveryDto
{
    public long Id { get; set; }
    public Guid IdempotencyKey { get; set; }
    public NotificationChannelType Channel { get; set; }
    public string Destination { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public DeliveryState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? SentAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
    public string? FailureReason { get; set; }
}
