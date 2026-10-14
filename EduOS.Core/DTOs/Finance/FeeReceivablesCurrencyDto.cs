namespace EduOS.Core.DTOs.Finance;
public sealed class FeeReceivablesCurrencyDto
{
    public string CurrencyCode { get; set; } = string.Empty;
    public int InvoiceCount { get; set; }
    public decimal Outstanding { get; set; }
    public decimal Current { get; set; }
    public decimal Days1To30 { get; set; }
    public decimal Days31To60 { get; set; }
    public decimal Days61To90 { get; set; }
    public decimal DaysOver90 { get; set; }
}
