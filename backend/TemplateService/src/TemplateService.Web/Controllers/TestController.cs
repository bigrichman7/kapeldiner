using Microsoft.AspNetCore.Mvc;

namespace TemplateService.Web.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TestController : ControllerBase
{
    [HttpGet]
    public IActionResult GetInfo()
    {
        return Ok(new
        {
            message = "Test controller is working"
        });
    }
}