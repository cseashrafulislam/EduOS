namespace EduOS.Core.DTOs.Transport;

/// <summary>Idempotent split of an operating rider reservation; fare and stops remain unchanged.</summary>
public sealed class TransferOperatingTransportVehicleRequestDto
{
    [Required] public Guid ClientRequestId { get; set; }
    [Required] public Guid VehicleReference { get; set; }
    [Required] public DateOnly EffectiveDate { get; set; }
    [Required] public string RowVersion { get; set; } = string.Empty;
    public long? FinanceClearanceId { get; set; }
}
