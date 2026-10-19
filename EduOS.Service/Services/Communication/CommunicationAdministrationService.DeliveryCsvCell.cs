namespace EduOS.Service.Services.Communication;

public sealed partial class CommunicationAdministrationService
{
    // Quote all fields, neutralize spreadsheet formulas and normalize embedded line breaks.
    private static string DeliveryCsvCell(string? value)
    {
        var text = (value ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        var first = text.TrimStart();
        if (first.Length > 0 && (first[0] == '=' || first[0] == '+' || first[0] == '-' || first[0] == '@' || char.IsControl(first[0])))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
