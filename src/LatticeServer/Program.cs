using LatticeServer.Services;

var builder = WebApplication.CreateBuilder(args);

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

app.Run();

// Enable WebApplicationFactory access for integration tests
public partial class Program { }
