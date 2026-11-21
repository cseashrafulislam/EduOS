using EduOS.Core.Common;
using EduOS.Core.DTOs.Platform;
using EduOS.Core.Interfaces.IServices;
using EduOS.Service.Services.AI;
using Moq;
using Xunit;

namespace EduOS.Tests.Services;

public sealed class AiInstitutionMetricServiceTests
{
    private static readonly DateOnly From = new(2026, 11, 1);
    private static readonly DateOnly To = new(2026, 11, 20);

    private static ApiResponse<CentralInstitutionMisDto> Report(
        bool available = true, decimal? value = 123456789012345.67m) =>
        ApiResponse<CentralInstitutionMisDto>.SuccessResponse(new CentralInstitutionMisDto
        {
            PeriodFrom = From, PeriodTo = To,
            GeneratedAtUtc = new DateTime(2026, 11, 21, 0, 0, 0, DateTimeKind.Utc),
            Metrics = new[]
            {
                new CentralInstitutionMisMetricDto
                {
                    Key = "outstanding_fee_amount", ModuleCode = "FINANCE",
                    Unit = "BDT", Definition = "Current operational receivables",
                    Source = "StudentInvoices", Scope = "Current",
                    Available = available, Value = available ? value : null
                }
            }
        });

    [Fact]
    public async Task Value_is_exact_canonical_decimal_text_and_scope_is_not_reinterpreted()
    {
        var m = new Mock<ICentralInstitutionMisService>();
        m.Setup(x => x.GetAsync(From, To, It.IsAny<CancellationToken>())).ReturnsAsync(Report());
        var result = await new AiInstitutionMetricService(m.Object)
            .AskAsync("বকেয়া ফি", From, To, CancellationToken.None);
        Assert.Equal(200, result.StatusCode);
        Assert.Equal("123456789012345.67", result.Data!.Value);
        Assert.Equal("Current", result.Data.Scope);
        Assert.Equal("StudentInvoices", result.Data.Source);
        m.Verify(x => x.GetAsync(From, To, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disabled_module_is_unavailable_not_zero()
    {
        var m = new Mock<ICentralInstitutionMisService>();
        m.Setup(x => x.GetAsync(From, To, It.IsAny<CancellationToken>())).ReturnsAsync(Report(false));
        var result = await new AiInstitutionMetricService(m.Object)
            .AskAsync("outstanding fee amount", From, To, CancellationToken.None);
        Assert.Equal(403, result.StatusCode);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task Unsupported_question_and_bad_period_never_query_mis()
    {
        var m = new Mock<ICentralInstitutionMisService>(MockBehavior.Strict);
        var svc = new AiInstitutionMetricService(m.Object);
        Assert.Equal(400, (await svc.AskAsync("fees for student 1", From, To, CancellationToken.None)).StatusCode);
        Assert.Equal(400, (await svc.AskAsync("fee dues", To, From, CancellationToken.None)).StatusCode);
        Assert.Equal(400, (await svc.AskAsync("fee dues", From, From.AddDays(366), CancellationToken.None)).StatusCode);
        m.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Canonical_mis_integrity_failure_is_preserved()
    {
        var m = new Mock<ICentralInstitutionMisService>();
        m.Setup(x => x.GetAsync(From, To, It.IsAny<CancellationToken>()))
            .ReturnsAsync(ApiResponse<CentralInstitutionMisDto>.ErrorResponse("GL integrity error", 409));
        var result = await new AiInstitutionMetricService(m.Object)
            .AskAsync("posted income", From, To, CancellationToken.None);
        Assert.Equal(409, result.StatusCode);
    }
}
