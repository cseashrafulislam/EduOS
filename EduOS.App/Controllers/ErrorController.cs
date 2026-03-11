using Microsoft.AspNetCore.Mvc;

namespace EduOS.App.Controllers
{
    public class ErrorController : Controller
    {
        [Route("Error/404")]
        public IActionResult NotFoundPage()
        {
            return View("NotFound");
        }

        [Route("Error/403")]
        public IActionResult AccessDenied()
        {
            return View("AccessDenied");
        }

        [Route("Error/500")]
        public IActionResult ServerError()
        {
            return View("ServerError");
        }
    }
}