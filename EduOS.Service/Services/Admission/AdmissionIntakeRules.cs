using EduOS.Core.DTOs.Admission;
using EduOS.Core.Enums;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EduOS.Service.Services.Admission;

internal static partial class AdmissionIntakeRules
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".pdf", ".doc", ".docx"
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string? ValidateForm(AdmissionIntakeFormInputDto request)
    {
        if (request == null) return "Admission form is required.";
        var code = request.Code?.Trim();
        if (string.IsNullOrWhiteSpace(code) || code.Length > 50 || !CodeRegex().IsMatch(code)) return "Form code is invalid.";
        if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Trim().Length > 200) return "Form title is required.";
        if (request.Description?.Trim().Length > 2000) return "Form description is too long.";
        if (request.AcademicYearId <= 0 || request.CampusId <= 0 || request.AcademicUnitId <= 0 || request.AcademicTermId is <= 0) return "Academic references are invalid.";
        if (request.OpensAtUtc == default || request.ClosesAtUtc == default || request.ClosesAtUtc <= request.OpensAtUtc) return "Form closing time must be after its opening time.";
        if (request.ApplicationFee < 0 || request.ApplicationFee > 1_000_000m) return "Application fee is invalid.";
        if (request.Currency == null || request.Currency.Trim().Length != 3 || request.Currency.Any(x => !char.IsLetter(x))) return "Currency must be a three-letter code.";
        if (request.Fields == null || request.DocumentRequirements == null) return "Form fields and document requirements are required.";
        if (request.Fields.Count > 50) return "An admission form cannot contain more than 50 custom fields.";
        if (request.DocumentRequirements.Count > 20) return "An admission form cannot contain more than 20 document requirements.";

        var fieldKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in request.Fields)
        {
            if (field == null) return "Custom field definitions are invalid.";
            var key = field.Key?.Trim();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 50 || !FieldKeyRegex().IsMatch(key) || !fieldKeys.Add(key)) return "Custom field keys must be unique and use letters, numbers or underscores.";
            if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Trim().Length > 200 || field.LabelBangla?.Trim().Length > 200) return "Custom field labels are invalid.";
            if (!Enum.IsDefined(field.Type)) return "Custom field type is invalid.";
            if (field.MaxLength is < 1 or > 4000) return "Custom field maximum length is invalid.";
            if (field.Options == null || field.Options.Count > 50 || field.Options.Any(x => string.IsNullOrWhiteSpace(x) || x.Trim().Length > 200)) return "Custom field options are invalid.";
            if (field.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != field.Options.Count) return "Custom field options must be unique.";
            if (field.Type == AdmissionFormFieldType.Choice && field.Options.Count == 0) return "Choice fields require at least one option.";
            if (field.Type != AdmissionFormFieldType.Choice && field.Options.Count > 0) return "Only choice fields can define options.";
        }

        var documentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in request.DocumentRequirements)
        {
            if (requirement == null) return "Document requirements are invalid.";
            var type = requirement.DocumentType?.Trim();
            if (string.IsNullOrWhiteSpace(type) || type.Length > 50 || !CodeRegex().IsMatch(type) || !documentTypes.Add(type)) return "Document types must be unique and use letters, numbers, hyphens or underscores.";
            if (string.IsNullOrWhiteSpace(requirement.Label) || requirement.Label.Trim().Length > 200 || requirement.LabelBangla?.Trim().Length > 200) return "Document labels are invalid.";
            if (requirement.MaxFileSizeMb is < 1 or > 10) return "Document size limit is invalid.";
            if (requirement.AllowedExtensions == null || requirement.AllowedExtensions.Count == 0 || requirement.AllowedExtensions.Any(x => !AllowedExtensions.Contains(NormalizeExtension(x)))) return "A document requirement contains an unsupported file extension.";
            if (requirement.AllowedExtensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requirement.AllowedExtensions.Count) return "Document extensions must be unique.";
        }

        return null;
    }

    public static string? ValidateResponses(
        IReadOnlyList<AdmissionFormFieldDto> fields,
        IReadOnlyDictionary<string, string?>? supplied,
        out string json)
    {
        json = "{}";
        supplied ??= new Dictionary<string, string?>();
        var definitions = fields.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        if (supplied.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != supplied.Count) return "Custom field keys must be unique.";
        if (supplied.Keys.Any(x => !definitions.ContainsKey(x))) return "The application contains an unknown custom field.";
        var normalized = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in fields.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            supplied.TryGetValue(field.Key, out var raw);
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (field.IsRequired && value == null) return $"{field.Label} is required.";
            if (value == null)
            {
                if (supplied.Keys.Any(x => string.Equals(x, field.Key, StringComparison.OrdinalIgnoreCase))) normalized[field.Key] = null;
                continue;
            }
            var maximum = field.MaxLength ?? 4000;
            if (value.Length > maximum) return $"{field.Label} exceeds its allowed length.";
            if (field.Type == AdmissionFormFieldType.Number && !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) return $"{field.Label} must be a number.";
            if (field.Type == AdmissionFormFieldType.Date && !DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _)) return $"{field.Label} must be a date.";
            if (field.Type == AdmissionFormFieldType.Boolean && !bool.TryParse(value, out _)) return $"{field.Label} must be true or false.";
            if (field.Type == AdmissionFormFieldType.Choice && !field.Options.Contains(value, StringComparer.OrdinalIgnoreCase)) return $"{field.Label} contains an invalid option.";
            normalized[field.Key] = value;
        }
        json = JsonSerializer.Serialize(normalized, JsonOptions);
        return json.Length > 65_536 ? "Custom responses are too large." : null;
    }

    public static string SerializeFields(IEnumerable<AdmissionFormFieldDto> fields) => JsonSerializer.Serialize(
        fields.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase).Select(x => new AdmissionFormFieldDto
        {
            Key = x.Key.Trim(),
            Label = x.Label.Trim(),
            LabelBangla = Trim(x.LabelBangla),
            Type = x.Type,
            IsRequired = x.IsRequired,
            MaxLength = x.MaxLength,
            DisplayOrder = x.DisplayOrder,
            Options = x.Options.Select(y => y.Trim()).ToList()
        }), JsonOptions);

    public static string SerializeRequirements(IEnumerable<AdmissionDocumentRequirementDto> requirements) => JsonSerializer.Serialize(
        requirements.OrderBy(x => x.DocumentType, StringComparer.OrdinalIgnoreCase).Select(x => new AdmissionDocumentRequirementDto
        {
            DocumentType = x.DocumentType.Trim(),
            Label = x.Label.Trim(),
            LabelBangla = Trim(x.LabelBangla),
            IsRequired = x.IsRequired,
            MaxFileSizeMb = x.MaxFileSizeMb,
            AllowedExtensions = x.AllowedExtensions.Select(NormalizeExtension).OrderBy(y => y, StringComparer.OrdinalIgnoreCase).ToList()
        }), JsonOptions);

    public static List<AdmissionFormFieldDto> ReadFields(string json) => Read<List<AdmissionFormFieldDto>>(json) ?? new();
    public static List<AdmissionDocumentRequirementDto> ReadRequirements(string json) => Read<List<AdmissionDocumentRequirementDto>>(json) ?? new();
    public static Dictionary<string, string?> ReadResponses(string? json) => string.IsNullOrWhiteSpace(json) ? new() : Read<Dictionary<string, string?>>(json) ?? new();

    public static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    public static string NormalizeExtension(string value)
    {
        var trimmed = (value ?? string.Empty).Trim().ToLowerInvariant();
        return trimmed.StartsWith('.') ? trimmed : "." + trimmed;
    }

    private static T? Read<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json, JsonOptions); }
        catch (JsonException) { return default; }
    }

    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex FieldKeyRegex();
}
