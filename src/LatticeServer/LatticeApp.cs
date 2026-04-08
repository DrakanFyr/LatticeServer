using LatticeServer.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LatticeServer;

public class LatticeApp
{
    public static WebApplication CreateApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // When the host project (Desktop, Android) is different from the library,
            // wwwroot lives in the output directory, not the source content root.
            WebRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot")
        });

        // When the host project (Desktop, Android) is different from the library,
        // the content root won't contain appsettings.json. Load it from the output
        // directory so Kestrel endpoints and other settings are picked up.
        var baseSettings = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(baseSettings))
        {
            builder.Configuration.AddJsonFile(baseSettings, optional: false, reloadOnChange: false);
            var env = builder.Environment.EnvironmentName;
            var envSettings = Path.Combine(AppContext.BaseDirectory, $"appsettings.{env}.json");
            builder.Configuration.AddJsonFile(envSettings, optional: true, reloadOnChange: false);
        }

        // Allow caller (e.g. Android host) to register overrides before defaults
        configure?.Invoke(builder);

        builder.Services.AddGrpc();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:5173", "https://localhost:5173", "http://localhost:5007")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
            });
        });
        builder.Services.AddControllers()
            .AddApplicationPart(typeof(LatticeApp).Assembly)
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });

        builder.Services.AddSingleton<EntityStore>();
        builder.Services.AddSingleton<TaskStore>();
        builder.Services.AddSingleton<ObjectStore>();
        // TryAdd: if configure already registered an ITemplateSource, this default is skipped
        builder.Services.TryAddSingleton<ITemplateSource, FileSystemTemplateSource>();
        builder.Services.AddSingleton<TemplateRegistry>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<TemplateRegistry>());
        builder.Services.AddSingleton<SpawnedEntityManager>();
        builder.Services.AddHostedService<ScenarioService>();

        builder.WebHost.UseShutdownTimeout(TimeSpan.FromSeconds(2));

        var app = builder.Build();

        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Register(() =>
        {
            app.Services.GetRequiredService<EntityStore>().Shutdown();
            app.Services.GetRequiredService<TaskStore>().Shutdown();
        });

        app.UseCors();
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseGrpcWeb();

        app.MapGrpcService<EntityManagerService>().EnableGrpcWeb();
        app.MapGrpcService<TaskManagerService>().EnableGrpcWeb();
        app.MapControllers();

        return app;
    }
}
