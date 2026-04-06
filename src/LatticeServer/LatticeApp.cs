using LatticeServer.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace LatticeServer;

public class LatticeApp
{
    public static WebApplication CreateApp(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Allow caller (e.g. Android host) to register overrides before defaults
        configure?.Invoke(builder);

        builder.Services.AddGrpc();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins("http://localhost:5173", "https://localhost:5173")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding");
            });
        });
        builder.Services.AddControllers()
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
