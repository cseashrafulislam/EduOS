using System.Text.Json;
using EduOS.Core.Common;

namespace EduOS.App.Controllers.Api;

/// <summary>
/// Two bounded reads through existing tenant/campus-authorized services.
/// Rejects count, identity, authorization and evidence drift before emitting CSV.
/// Not a SQL transaction-level snapshot.
/// </summary>
internal static class ProcurementCsvExportGuard
{
    internal static async Task<(List<T>? Rows, int ErrorStatus)> ReadAsync<T, TKey>(
        Func<int, Task<ApiResponse<PagedResult<T>>>> fetch, Func<T, TKey> key)
        where T : class where TKey : notnull
    {
        List<T>? original = null;
        string? signature = null;
        int originalCount = -1;
        for (int pass = 0; pass < 2; pass++)
        {
            var rows = new List<T>();
            var seen = new HashSet<TKey>();
            int total = -1;
            for (int page = 1; ; page++)
            {
                var response = await fetch(page);
                if (!response.Success)
                    return (null, response.StatusCode is >= 400 and <= 599 ? response.StatusCode : 409);
                var result = response.Data;
                if (result == null || result.Page != page || result.PageSize != 100 ||
                    result.TotalCount < 0 || result.TotalCount > 500)
                    return (null, result is { TotalCount: > 500 } ? 413 : 409);
                if (total < 0) total = result.TotalCount;
                if (result.TotalCount != total || (pass == 1 && total != originalCount) ||
                    result.Items == null || result.Items.Count != Math.Min(100, total - rows.Count))
                    return (null, 409);
                foreach (var item in result.Items)
                {
                    if (item == null) return (null, 409);
                    var id = key(item);
                    if (id == null || EqualityComparer<TKey>.Default.Equals(id, default!) || !seen.Add(id))
                        return (null, 409);
                }
                rows.AddRange(result.Items);
                if (page >= Math.Max(1, (total + 99) / 100)) break;
            }
            if (rows.Count != total) return (null, 409);
            var current = JsonSerializer.Serialize(rows);
            if (pass == 0) { original = rows; signature = current; originalCount = total; }
            else if (!string.Equals(signature, current, StringComparison.Ordinal)) return (null, 409);
        }
        return (original, 0);
    }

    internal static string Cell(object? value)
    {
        var s = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
        var numeric = value is sbyte or byte or short or ushort or int or uint or long or ulong
            or float or double or decimal;
        s = new string(s.Select(c => char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c).ToArray());
        var trimmed = s.TrimStart();
        if (!numeric && trimmed.Length > 0 && "=+-@".Contains(trimmed[0])) s = "'" + s;
        return "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
