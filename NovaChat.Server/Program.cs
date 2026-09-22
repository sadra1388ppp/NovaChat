using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NovaChat.Server.Authorization;
using NovaChat.Server.Data;
using NovaChat.Server.Hubs;
using NovaChat.Server.Middleware;
using NovaChat.Server.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

var elasticsearchUrl = builder.Configuration["Elasticsearch:Url"];
var elasticsearchUsername = builder.Configuration["Elasticsearch:Username"];
var elasticsearchPassword = builder.Configuration["Elasticsearch:Password"];
var elasticsearchFingerprint = builder.Configuration["Elasticsearch:Fingerprint"];

if (string.IsNullOrWhiteSpace(elasticsearchUrl))
    throw new InvalidOperationException("Elasticsearch URL is not configured.");

if (string.IsNullOrWhiteSpace(elasticsearchUsername))
    throw new InvalidOperationException("Elasticsearch username is not configured.");

if (string.IsNullOrWhiteSpace(elasticsearchPassword))
    throw new InvalidOperationException("Elasticsearch password is not configured.");

if (string.IsNullOrWhiteSpace(elasticsearchFingerprint))
    throw new InvalidOperationException("Elasticsearch certificate fingerprint is not configured.");

var elasticsearchSettings = new ElasticsearchClientSettings(new Uri(elasticsearchUrl))
    .CertificateFingerprint(elasticsearchFingerprint)
    .Authentication(new BasicAuthentication(elasticsearchUsername, elasticsearchPassword));

builder.Services.AddSingleton(new ElasticsearchClient(elasticsearchSettings));
builder.Services.AddSingleton<ElasticsearchMessageService>();
builder.Services.AddSingleton<ElasticsearchHttpRequestLogService>();

builder.Services.AddDbContext<AppDbContext>(options => options.UseNovaChatDatabase(builder.Configuration));
builder.Services.AddDbContext<HttpRequestLogDbContext>(options => options.UseNovaChatHttpLogDatabase(builder.Configuration));
builder.Services.AddScoped<DatabaseInitializer>();
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<ContactService>();
builder.Services.AddSingleton<PasswordHashService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddSingleton<JwtTokenRevocationService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<ChatRequestService>();
builder.Services.AddScoped<GroupAddRequestService>();
builder.Services.AddScoped<MessageReadService>();
builder.Services.AddScoped<E2eeDeviceService>();
builder.Services.AddScoped<HttpRequestLogService>();
builder.Services.AddSingleton<PresenceService>();
builder.Services.AddSingleton<IAuthorizationHandler, OwnerAuthorizationHandler>();
builder.Services.AddAuthorization(options => options.AddPolicy("OwnerOnly", policy => { policy.RequireAuthenticatedUser(); policy.AddRequirements(new OwnerRequirement()); }));
var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey)) throw new InvalidOperationException("JWT Key is not configured.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters { ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)), ValidateIssuer = true, ValidIssuer = builder.Configuration["Jwt:Issuer"], ValidateAudience = true, ValidAudience = builder.Configuration["Jwt:Audience"], ValidateLifetime = true, ClockSkew = TimeSpan.Zero };
    options.Events = new JwtBearerEvents { OnMessageReceived = context => { var accessToken = context.Request.Query["access_token"]; var path = context.HttpContext.Request.Path; if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat")) context.Token = accessToken; return Task.CompletedTask; } };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Name = "Authorization", Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Description = "Enter your JWT token." });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] });
});

var app = builder.Build();
try
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<DatabaseInitializer>().ValidateSchemaAsync();
    var httpRequestLogDb = scope.ServiceProvider.GetRequiredService<HttpRequestLogDbContext>();
    await httpRequestLogDb.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS AuditLogs;");
    await httpRequestLogDb.Database.EnsureCreatedAsync();

    await scope.ServiceProvider
        .GetRequiredService<ElasticsearchMessageService>()
        .EnsureIndexAsync();

    await scope.ServiceProvider
        .GetRequiredService<ElasticsearchHttpRequestLogService>()
        .EnsureIndexAsync();
}
catch (Exception exception) when (exception is not OperationCanceledException)
{
    app.Logger.LogCritical(exception, "NovaChat server startup database initialization failed. The HTTP listener was not started.");
    throw;
}

var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
Directory.CreateDirectory(Path.Combine(webRoot, "uploads", "avatars"));
if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }
app.UseMiddleware<HttpRequestLoggingMiddleware>();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseMiddleware<JwtTokenRevocationMiddleware>();
app.UseAuthorization();
app.UseMiddleware<ChatPrivacyMiddleware>();
app.MapControllers();

app.MapHub<ChatHub>("/hubs/chat");
app.Run();
