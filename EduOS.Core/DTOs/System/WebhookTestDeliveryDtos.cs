namespace EduOS.Core.DTOs.System;

/// <summary>Idempotent administrator-initiated test of an existing webhook endpoint.</summary>
public sealed class CreateWebhookTestRequestDto
{
    public Guid ClientRequestId { get; set; }
}

/// <summary>Retries only the attempt count the administrator actually reviewed.</summary>
public sealed class RetryWebhookDeliveryRequestDto
{
    public Guid ClientRequestId { get; set; }
    public int ExpectedAttemptCount { get; set; }
}
