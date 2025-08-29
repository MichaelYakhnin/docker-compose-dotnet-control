
using Microsoft.AspNetCore.Mvc;
using Docker.DotNet.Models;
using System.Threading.Tasks;
using docker_compose_dotnet_control;

namespace docker_compose_dotnet_control.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ComposeController : ControllerBase
    {
        private readonly DockerService _dockerService;
        private readonly ComposeFileParser _parser;

        public ComposeController()
        {
            _dockerService = new DockerService();
            _parser = new ComposeFileParser();
        }
        [HttpPost("up")]
    public async Task<IActionResult> Up([FromQuery] string composeFile, string folder)
        {
            if (string.IsNullOrEmpty(composeFile) || !System.IO.File.Exists(composeFile))
                return BadRequest("Compose file not found.");

            var services = _parser.Parse(composeFile);
            if (services.Count == 0)
                return BadRequest("No services found in compose file.");

            await _dockerService.UpAsync(services, folder);
            return Ok("Containers started.");
        }

        [HttpPost("down")]
    public async Task<IActionResult> Down([FromQuery] string composeFile)
        {
            if (string.IsNullOrEmpty(composeFile) || !System.IO.File.Exists(composeFile))
                return BadRequest("Compose file not found.");

            var services = _parser.Parse(composeFile);
            if (services.Count == 0)
                return BadRequest("No services found in compose file.");

            await _dockerService.DownAsync(services);
            return Ok("Containers stopped and removed.");
        }
    }
}
