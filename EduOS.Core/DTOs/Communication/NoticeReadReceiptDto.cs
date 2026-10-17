namespace EduOS.Core.DTOs.Communication;

public sealed class NoticeReadReceiptDto
{
    public long UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime ReadAt { get; set; }
}
