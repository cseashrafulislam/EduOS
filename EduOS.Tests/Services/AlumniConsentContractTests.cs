using System.Reflection;
using EduOS.App.Authorization;
using EduOS.App.Controllers.Api;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AlumniConsentContractTests
{
    [Fact]
    public void All_alumni_routes_require_the_dedicated_entitlement_and_csrf_on_writes()
    {
        var api = typeof(AlumniApiController);
        Assert.Contains(api.GetCustomAttributes(typeof(RequireModuleAttribute), true),
            x => ((RequireModuleAttribute)x).Policy == "EduOSModule:ALUMNI");
        Assert.NotEmpty(api.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.AutoValidateAntiforgeryTokenAttribute), true));
        var directory = api.GetMethod(nameof(AlumniApiController.Directory))!;
        Assert.Contains(directory.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            x => ((AuthorizeAttribute)x).Roles == "TenantAdmin");
        var export = api.GetMethod(nameof(AlumniApiController.ExportPage))!;
        Assert.Contains(export.GetCustomAttributes(typeof(AuthorizeAttribute), true),
            x => ((AuthorizeAttribute)x).Roles == "TenantAdmin");
    }

    [Fact]
    public void Consent_is_bound_to_verified_student_completion_and_cannot_be_published_without_opt_in()
    {
        var source = Read("EduOS.Service", "Services", "Alumni", "AlumniService.cs");
        Assert.Contains("student.UserId == userId", source);
        Assert.Contains("student.TenantId == tenantId", source);
        Assert.Contains("exit.ExitType == StudentExitType.Completed", source);
        Assert.Contains("exit.FeesCleared", source);
        Assert.Contains("enrollment.State == EnrollmentState.Completed", source);
        Assert.Contains("IsolationLevel.Serializable", source);
        Assert.Contains("CryptographicOperations.FixedTimeEquals", source);
        Assert.Contains("DirectoryOptIn = 1", source);
        Assert.Contains("p.CampusId = en.CampusId", source);
        Assert.Contains("UserCampusAccesses", source);
        Assert.Contains("AlumniConsentCommands", source);
        Assert.Contains("AlumniProfileAudits", source);
        Assert.DoesNotContain("IgnoreQueryFilters", source);
    }

    [Fact]
    public void Migration_enforces_tenant_owned_unique_profiles_and_private_contact_clearing()
    {
        var source = Read("EduOS.Persistence", "Migrations", "20261031220000_V3AlumniConsentDirectory.cs");
        Assert.Contains("UX_AlumniProfiles_Tenant_Student", source);
        Assert.Contains("UX_AlumniConsentCommands_Tenant_Request", source);
        Assert.Contains("CK_AlumniProfiles_PrivateContact", source);
        Assert.Contains("StudentExitRecordId", source);
        Assert.Contains("RowVersion", source);
    }


    [Theory]
    [InlineData(" =1+1")]
    [InlineData("\t=1+1")]
    [InlineData("\r\n@HYPERLINK(\"https://example.invalid\",\"x\")")]
    [InlineData("\u00A0+SUM(1,2)")]
    [InlineData("\uFEFF@SUM(1,2)")]
    [InlineData("\u200B-1+1")]
    public void Alumni_csv_exports_force_text_for_formulas_after_invisible_prefix(string input)
    {
        var csv = InvokeCsv(input);
        Assert.Equal("\"'" + input.Replace("\"", "\"\"") + "\"", csv);
    }

    [Theory]
    [InlineData("Alice", "\"Alice\"")]
    [InlineData("A \"quoted\" name", "\"A \"\"quoted\"\" name\"")]
    [InlineData(null, "\"\"")]
    public void Alumni_csv_exports_preserve_ordinary_quoted_values(string? input, string expected)
    {
        Assert.Equal(expected, InvokeCsv(input));
    }

    [Fact]
    public void Alumni_csv_headers_and_event_statuses_follow_selected_language_without_changing_utc_values()
    {
        Assert.Equal("\uFEFFStudent code,Name,Email,Phone,Completed (UTC),Campus ID\r\n",
            InvokePrivate("DirectoryHeader", false));
        Assert.Equal("\uFEFFশিক্ষার্থী কোড,নাম,ইমেইল,ফোন,সম্পন্ন (UTC),ক্যাম্পাস আইডি\r\n",
            InvokePrivate("DirectoryHeader", true));
        Assert.Equal("\uFEFFEvent,Campus ID,Starts (UTC),Status,Going,Declined,Checked in\r\n",
            InvokePrivate("EngagementHeader", false));
        Assert.Equal("\uFEFFঅনুষ্ঠান,ক্যাম্পাস আইডি,শুরু (UTC),অবস্থা,আসবেন,আসবেন না,উপস্থিত\r\n",
            InvokePrivate("EngagementHeader", true));
        Assert.Equal("Draft", InvokePrivate("EventStatusLabel", 1, false));
        Assert.Equal("Published", InvokePrivate("EventStatusLabel", 2, false));
        Assert.Equal("Cancelled", InvokePrivate("EventStatusLabel", 3, false));
        Assert.Equal("খসড়া", InvokePrivate("EventStatusLabel", 1, true));
        Assert.Equal("প্রকাশিত", InvokePrivate("EventStatusLabel", 2, true));
        Assert.Equal("বাতিল", InvokePrivate("EventStatusLabel", 3, true));
        var source = Read("EduOS.App", "Controllers", "Api", "AlumniApiController.cs");
        Assert.Equal(2, source.Split("CultureInfo.CurrentUICulture.TwoLetterISOLanguageName").Length - 1);
        Assert.Contains("Csv(EventStatusLabel(item.Status, bengali))", source);
        Assert.Contains("DirectoryAsync(query, ct)", source);
        Assert.Contains("EventsAsync(query, ct)", source);
        Assert.Contains("[Authorize(Roles = \"TenantAdmin\")]", source);
    }

    private static string InvokePrivate(string name, params object[] arguments)
    {
        var method = typeof(AlumniApiController).GetMethod(name,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, arguments));
    }

    private static string InvokeCsv(string? input)
    {
        var method = typeof(AlumniApiController).GetMethod("Csv",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsType<string>(method!.Invoke(null, new object?[] { input }));
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var file = Path.Combine(new[] { directory.FullName }.Concat(segments).ToArray());
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join("/", segments));
    }
}
