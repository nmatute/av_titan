using DroneDetection.Models;
using DroneDetection.Services;
using Microsoft.AspNetCore.Mvc;

namespace DroneDetection.Controllers;

// [ApiController] makes ASP.NET check the incoming JSON against the model rules
// send back a 400 error by itself if something is wrong.
[ApiController]
public class DetectionsController : ControllerBase
{
    private readonly DetectionStore _store;

    // ASP.NET gives us the one shared DetectionStore (registered in Program.cs).
    public DetectionsController(DetectionStore store)
    {
        _store = store;
    }

    // The simulator posts here every 3 seconds.
    [HttpPost("/detections")]
    public IActionResult Receive(DetectionMessage message)
    {
        _store.AddMessage(message);
        return NoContent();   // 204: "got it, nothing to send back"
    }

    // The dashboard calls this every second.
    [HttpGet("/api/state")]
    public ActionResult<StateResponse> GetState()
    {
        return _store.GetState();
    }

    // The dashboard's Clear button.
    [HttpPost("/api/clear")]
    public IActionResult Clear()
    {
        _store.Clear();
        return NoContent();
    }
}