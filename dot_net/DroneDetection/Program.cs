using System.Text.Json.Serialization;
using DroneDetection.Services;

public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // MVC: controllers plus Razor views.
        builder.Services.AddControllersWithViews().AddJsonOptions(options =>
        {
            // the timestamp arrives as a string ("1712010300"), so allow reading numbers from strings
            options.JsonSerializerOptions.NumberHandling = JsonNumberHandling.AllowReadingFromString;
            // reject JSON nulls for fields that aren't nullable
            options.JsonSerializerOptions.RespectNullableAnnotations = true;
        });

        // Dependency injection: register services that controllers can use.
        builder.Services.AddSingleton(TimeProvider.System);   // the real clock
        builder.Services.AddSingleton<DetectionStore>();      // one shared store for the whole app

        var app = builder.Build();

        app.UseStaticFiles();   // serves wwwroot (css, js)

        app.MapControllerRoute(
            name: "default",
            pattern: "{controller=Home}/{action=Index}");

        // Map actions that use attribute routes, including /detections and /api/state.
        app.MapControllers();

        app.Run();
    }
}