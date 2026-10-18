using EduOS.Service.Services.Platform;
using Xunit;
namespace EduOS.Tests.Services;
public sealed class ApiCredentialAuthorizationParserTests
{
    [Theory]
    [InlineData("Bearer token")]
    [InlineData("ApiKey eduos_invalid")]
    [InlineData("ApiKey  eduos_invalid")]
    [InlineData("")]
    public void Invalid_header_is_rejected(string header) =>
        Assert.False(ApiCredentialUsageService.TryParseAuthorization(header, out _));
}
