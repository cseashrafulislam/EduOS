namespace EduOS.Core.DTOs.System;

public class NumberSeriesDto
{
    public long Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public long NextValue { get; set; }
    public int Padding { get; set; }
    public string? Suffix { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveNumberSeriesRequestDto
{
    [Required, MaxLength(100)] public string Key { get; set; } = string.Empty;
    [MaxLength(50)] public string Prefix { get; set; } = string.Empty;
    [Range(1, 30)] public int Padding { get; set; } = 6;
    [MaxLength(50)] public string? Suffix { get; set; }
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class CustomFieldDefinitionDto
{
    public long Id { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public CustomFieldDataType DataType { get; set; }
    public bool IsRequired { get; set; }
    public bool IsSensitive { get; set; }
    public int DisplayOrder { get; set; }
    public string? ValidationJson { get; set; }
    public bool IsActive { get; set; }
    public IReadOnlyList<CustomFieldOptionDto> Options { get; set; } = Array.Empty<CustomFieldOptionDto>();
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveCustomFieldDefinitionRequestDto
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
    public IReadOnlyList<SaveCustomFieldOptionRequestDto> Options { get; set; } = Array.Empty<SaveCustomFieldOptionRequestDto>();
    public string? RowVersion { get; set; }
}

public class CustomFieldOptionDto
{
    public long Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; }
}

public class SaveCustomFieldOptionRequestDto
{
    public long? Id { get; set; }
    [Required, MaxLength(100)] public string Value { get; set; } = string.Empty;
    [Required, MaxLength(150)] public string Label { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomFieldValueDto
{
    public long CustomFieldDefinitionId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? Value { get; set; }
}

public class SaveCustomFieldValueRequestDto
{
    public long CustomFieldDefinitionId { get; set; }
    [MaxLength(4000)] public string? Value { get; set; }
}

public class AuditLogDto
{
    public long Id { get; set; }
    public long? TenantId { get; set; }
    public long? UserId { get; set; }
    public string? UserName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityName { get; set; } = string.Empty;
    public long? EntityId { get; set; }
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public string? IpAddress { get; set; }
    public string? Endpoint { get; set; }
    public bool IsSuccess { get; set; }
    public DateTime OccurredAt { get; set; }
    public string? CorrelationId { get; set; }
    public string? RequestId { get; set; }
}

public class ImportLogItemDto
{
    public long Id { get; set; }
    public long ImportLogId { get; set; }
    public int RowNumber { get; set; }
    public string? BusinessKey { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ImportLogDto
{
    public long Id { get; set; }
    public Guid Reference { get; set; }
    public string ImportType { get; set; } = string.Empty;
    public long? FileAssetId { get; set; }
    public int TotalRows { get; set; }
    public int SuccessRows { get; set; }
    public int FailedRows { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorSummary { get; set; }
}

public class WebhookEndpointDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public bool HasSecret { get; set; }
    public IReadOnlyList<string> EventCodes { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class SaveWebhookEndpointRequestDto
{
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(1000)] public string Url { get; set; } = string.Empty;
    public string? Secret { get; set; }
    public bool ClearSecret { get; set; }
    public IReadOnlyList<string> EventCodes { get; set; } = Array.Empty<string>();
    public bool IsActive { get; set; } = true;
    public string? RowVersion { get; set; }
}

public class WebhookDeliveryDto
{
    public long Id { get; set; }
    public Guid EventId { get; set; }
    public long WebhookEndpointId { get; set; }
    public string EventCode { get; set; } = string.Empty;
    public WebhookDeliveryState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? LastAttemptAt { get; set; }
    public DateTime? NextAttemptAt { get; set; }
    public string? FailureReason { get; set; }
}

public class OutboxMessageDto
{
    public long Id { get; set; }
    public long? TenantId { get; set; }
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public OutboxState State { get; set; }
    public int AttemptCount { get; set; }
    public DateTime OccurredAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public string? FailureReason { get; set; }
}

public class ApiCredentialDto
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string KeyPrefix { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowedScopes { get; set; } = Array.Empty<string>();
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; }
    public string RowVersion { get; set; } = string.Empty;
}

public class CreateApiCredentialRequestDto
{
    public Guid ClientRequestId { get; set; }
    [Required, MaxLength(150)] public string Name { get; set; } = string.Empty;
    public IReadOnlyList<string> AllowedScopes { get; set; } = Array.Empty<string>();
    public DateTime? ExpiresAt { get; set; }
}

public class ApiCredentialCreatedDto
{
    public ApiCredentialDto Credential { get; set; } = new();
    public string PlainTextKey { get; set; } = string.Empty;
}

public class RevokeApiCredentialRequestDto
{
    [Required] public string RowVersion { get; set; } = string.Empty;
}
