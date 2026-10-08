using EduOS.Core.DTOs.Admission;
using EduOS.Core.Enums.Domain;
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
            var key = field.FieldKey?.Trim();
            if (string.IsNullOrWhiteSpace(key) || key.Length > 100 || !FieldKeyRegex().IsMatch(key) || !fieldKeys.Add(key))
                return "Custom field keys must be unique and use letters, numbers or underscores.";
            if (string.IsNullOrWhiteSpace(field.Label) || field.Label.Trim().Length > 150) return "Custom field labels are invalid.";
            if (!Enum.IsDefined(field.DataType)) return "Custom field type is invalid.";
            if (field.ValidationJson?.Length > 1000 || field.OptionsJson?.Length > 4000) return "Custom field validation/options are too long.";
            if (!string.IsNullOrWhiteSpace(field.ValidationJson) && !IsJsonObject(field.ValidationJson))
                return "Custom field validation must be a JSON object.";
            if (!TryReadOptions(field.OptionsJson, out var options))
                return "Custom field options must be a valid JSON string array.";
            if (options.Count > 50 || options.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 200))
                return "Custom field options are invalid.";
            if (options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != options.Count)
                return "Custom field options must be unique.";
            var choice = field.DataType is CustomFieldDataType.Select or CustomFieldDataType.MultiSelect;
            if (choice && options.Count == 0) return "Select fields require options.";
            if (!choice && options.Count > 0) return "Only select fields can define options.";
        }

        var documentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var requirement in request.DocumentRequirements)
        {
            if (requirement == null) return "Document requirements are invalid.";
            var type = requirement.DocumentType?.Trim();
            if (string.IsNullOrWhiteSpace(type) || type.Length > 50 || !CodeRegex().IsMatch(type) || !documentTypes.Add(type))
                return "Document types must be unique and use letters, numbers, hyphens or underscores.";
            if (string.IsNullOrWhiteSpace(requirement.Label) || requirement.Label.Trim().Length > 200 || requirement.LabelBangla?.Trim().Length > 200)
                return "Document labels are invalid.";
            if (requirement.MaxFileSizeMb is < 1 or > 10) return "Document size limit is invalid.";
            if (requirement.AllowedExtensions == null || requirement.AllowedExtensions.Count == 0 ||
                requirement.AllowedExtensions.Any(x => !AllowedExtensions.Contains(NormalizeExtension(x))))
                return "A document requirement contains an unsupported file extension.";
            if (requirement.AllowedExtensions.Select(NormalizeExtension).Distinct(StringComparer.OrdinalIgnoreCase).Count() != requirement.AllowedExtensions.Count)
                return "Document extensions must be unique.";
        }
        return null;
    }

    public static string? ValidateResponses(IReadOnlyList<AdmissionFormFieldDto> fields,
        IReadOnlyDictionary<string, string?>? supplied, out string json)
    {
        json = "{}";
        supplied ??= new Dictionary<string, string?>();
        if (fields == null) return "Field definitions are required.";
        var definitions = new Dictionary<string, AdmissionFormFieldDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (field == null || string.IsNullOrWhiteSpace(field.FieldKey) || !definitions.TryAdd(field.FieldKey, field))
                return "Form field definitions are invalid.";
        }
        if (supplied.Keys.Any(x => !definitions.ContainsKey(x))) return "The application contains an unknown custom field.";
        var normalized = new SortedDictionary<string, string?>(StringComparer.Ordinal);
        foreach (var field in fields.OrderBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase))
        {
            supplied.TryGetValue(field.FieldKey, out var raw);
            var value = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
            if (field.IsRequired && value == null) return $"{field.Label} is required.";
            if (value == null)
            {
                if (supplied.ContainsKey(field.FieldKey)) normalized[field.FieldKey] = null;
                continue;
            }
            if (value.Length > 4000) return $"{field.Label} exceeds its allowed length.";
            if (field.DataType is CustomFieldDataType.Number or CustomFieldDataType.Decimal &&
                !decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
                return $"{field.Label} must be a number.";
            if (field.DataType == CustomFieldDataType.Date &&
                !DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return $"{field.Label} must be a date.";
            if (field.DataType == CustomFieldDataType.DateTime &&
                !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _))
                return $"{field.Label} must be a date/time.";
            if (field.DataType == CustomFieldDataType.Boolean && !bool.TryParse(value, out _))
                return $"{field.Label} must be true or false.";
            if (field.DataType is CustomFieldDataType.Select or CustomFieldDataType.MultiSelect)
            {
                if (!TryReadOptions(field.OptionsJson, out var options)) return $"{field.Label} has invalid options.";
                if (field.DataType == CustomFieldDataType.Select && !options.Contains(value, StringComparer.OrdinalIgnoreCase))
                    return $"{field.Label} contains an invalid option.";
                if (field.DataType == CustomFieldDataType.MultiSelect)
                {
                    if (!TryReadOptions(value, out var selected) ||
                        selected.Count == 0 || selected.Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Count ||
                        selected.Any(x => !options.Contains(x, StringComparer.OrdinalIgnoreCase)))
                        return $"{field.Label} contains invalid selections.";
                }
            }
            if (field.DataType == CustomFieldDataType.Json && !IsJsonValid(value))
                return $"{field.Label} contains invalid JSON.";
            normalized[field.FieldKey] = value;
        }
        json = JsonSerializer.Serialize(normalized, JsonOptions);
        return json.Length > 65_536 ? "Custom responses are too large." : null;
    }

    public static string SerializeFields(IEnumerable<AdmissionFormFieldDto> fields) => JsonSerializer.Serialize(
        fields.OrderBy(x => x.DisplayOrder).ThenBy(x => x.FieldKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => new AdmissionFormFieldDto
            {
                Id = x.Id, FieldKey = x.FieldKey.Trim(), Label = x.Label.Trim(), DataType = x.DataType,
                IsRequired = x.IsRequired, DisplayOrder = x.DisplayOrder, OptionsJson = x.OptionsJson,
                ValidationJson = x.ValidationJson, IsActive = x.IsActive, RowVersion = x.RowVersion
            }), JsonOptions);

    public static string SerializeRequirements(IEnumerable<AdmissionDocumentRequirementDto> requirements) => JsonSerializer.Serialize(
        requirements.OrderBy(x => x.DocumentType, StringComparer.OrdinalIgnoreCase).Select(x => new AdmissionDocumentRequirementDto
        {
            DocumentType = x.DocumentType.Trim(), Label = x.Label.Trim(), LabelBangla = Trim(x.LabelBangla),
            IsRequired = x.IsRequired, MaxFileSizeMb = x.MaxFileSizeMb,
            AllowedExtensions = x.AllowedExtensions.Select(NormalizeExtension).OrderBy(y => y, StringComparer.OrdinalIgnoreCase).ToList()
        }), JsonOptions);

    public static List<AdmissionFormFieldDto> ReadFields(string json) => Read<List<AdmissionFormFieldDto>>(json) ?? new();
    public static List<AdmissionDocumentRequirementDto> ReadRequirements(string json) => Read<List<AdmissionDocumentRequirementDto>>(json) ?? new();
    public static Dictionary<string, string?> ReadResponses(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new() : Read<Dictionary<string, string?>>(json) ?? new();

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

    private static bool TryReadOptions(string? json, out List<string> values)
    {
        values = new List<string>();
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            values = JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? new();
            return values.All(x => x != null);
        }
        catch (JsonException) { return false; }
    }

    private static bool IsJsonObject(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return doc.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { return false; }
    }

    private static bool IsJsonValid(string json)
    {
        try { using var doc = JsonDocument.Parse(json); return true; }
        catch (JsonException) { return false; }
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
