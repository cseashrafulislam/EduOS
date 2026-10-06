using EduOS.Core.Entities.Base;

namespace EduOS.Core.Entities.System;

public class NumberSeries : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [MaxLength(50)] public string Prefix { get; set; } = string.Empty;
    public long NextValue { get; set; } = 1;
    public int Padding { get; set; } = 6;
    [MaxLength(50)] public string? Suffix { get; set; }
    public DateTime? LastIssuedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomFieldDefinition : BaseTenantEntity
{
    [Required, MaxLength(100)] public string EntityType { get; set; } = string.Empty;
    [Required, MaxLength(100)] public string FieldKey { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; } = CustomFieldDataType.Text;
    public bool IsRequired { get; set; }
    public bool IsSensitive { get; set; }
    public int DisplayOrder { get; set; }
    [MaxLength(1000)] public string? ValidationJson { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomFieldOption : BaseTenantEntity
{
    public long CustomFieldDefinitionId { get; set; }
    [Required, MaxLength(100)] public string Value { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomFieldValue : BaseTenantEntity
{
    public long CustomFieldDefinitionId { get; set; }
    public long EntityId { get; set; }
    [MaxLength(4000)] public string? Value { get; set; }
}

public class AuditLog : BaseEntity
{
    public long? TenantId { get; set; }
    public long? UserId { get; set; }
    [MaxLength(200)] public string? UserName { get; set; }
    [Required, MaxLength(100)] public string Action { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string EntityName { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    [MaxLength(100)] public string? IpAddress { get; set; }
    [MaxLength(500)] public string? Endpoint { get; set; }
    [MaxLength(100)] public string? CorrelationId { get; set; }
    [MaxLength(100)] public string? RequestId { get; set; }
    public bool IsSuccess { get; set; } = true;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class ImportLog : BaseTenantEntity
{
    public Guid PublicId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(100)] public string ImportType { get; set; } = string.Empty;
    public long? FileAssetId { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    [MaxLength(4000)] public string? ErrorSummary { get; set; }
}

public class WebhookEndpoint : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Url { get; set; } = string.Empty;
    [Required, MaxLength(4000)] public string ProtectedSecret { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string EventCodes { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class WebhookDelivery : BaseTenantEntity
{
    public Guid EventId { get; set; }
    public long WebhookEndpointId { get; set; }
    [Required, MaxLength(100)] public string EventCode { get; set; } = string.Empty;
    [Required] public string PayloadJson { get; set; } = string.Empty;
    public WebhookDeliveryState State { get; set; } = WebhookDeliveryState.Pending;
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    [MaxLength(2000)] public string? FailureReason { get; set; }
}

public class OutboxMessage : BaseEntity
{
    public long? TenantId { get; set; }
    public Guid EventId { get; set; } = Guid.NewGuid();
    [Required, MaxLength(200)] public string EventType { get; set; } = string.Empty;
    [Required] public string PayloadJson { get; set; } = string.Empty;
    public OutboxState State { get; set; } = OutboxState.Pending;
    public int AttemptCount { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    [MaxLength(2000)] public string? FailureReason { get; set; }
}

public class ApiCredential : BaseTenantEntity
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(30)] public string KeyPrefix { get; set; } = string.Empty;
    [Required, MaxLength(128)] public string KeyHash { get; set; } = string.Empty;
    [MaxLength(1000)] public string? AllowedScopes { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ImportLogItem : BaseTenantEntity
{
    public long ImportLogId { get; set; }
    public int RowNumber { get; set; }
    [MaxLength(200)] public string? BusinessKey { get; set; }
    public bool IsSuccess { get; set; }
    [MaxLength(4000)] public string? ErrorMessage { get; set; }
}

public class IdempotencyRecord : BaseTenantEntity
{
    [Required, MaxLength(100)] public string Scope { get; set; } = string.Empty;
    public Guid RequestId { get; set; }
    [MaxLength(128)] public string? RequestHash { get; set; }
    [MaxLength(128)] public string? ResultHash { get; set; }
    public DateTime ExpiresAt { get; set; }
}
