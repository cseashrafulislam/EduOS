using EduOS.App.Authorization;
using EduOS.Core.Common;
using EduOS.Core.Interfaces;
using EduOS.Core.Interfaces.IServices;
using EduOS.Persistence.Context;
using EduOS.Service.Services.Inventory;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Logging;
namespace EduOS.App.Controllers.Api;
[ApiController, Route("api/inventory/movements"), Authorize, RequireModule("INVENTORY"), AutoValidateAntiforgeryToken, EnableRateLimiting("ApiPolicy")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class InventoryMovementsController : ControllerBase
{
    private readonly IInventoryMovementService service;
    public InventoryMovementsController(IInventoryMovementService implementation) => service = implementation;
    private IActionResult Reply<T>(ApiResponse<T> result) => StatusCode(result.StatusCode, result);
}
