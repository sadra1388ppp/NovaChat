using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using NovaChat.Server.Authorization;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddControllers();

builder.Services.AddSignalR();

builder.Services.AddScoped<UserService>();

builder.Services.AddScoped<
    IPasswordHasher<User>,
    PasswordHasher<User>>();

builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AdminService>();
builder.Services.AddScoped<ChatService>();

builder.Services.AddSingleton<
    IAuthorizationHandler,
    OwnerAuthorizationHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("OwnerOnly", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new OwnerRequirement());
    });
});

var jwtKey = builder.Configuration["Jwt:Key"];

if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException(
        "JWT Key is not configured.");
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters =
            new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,

                IssuerSigningKey =
                    new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(jwtKey)),

                ValidateIssuer = true,

                ValidIssuer =
                    builder.Configuration["Jwt:Issuer"],

                ValidateAudience = true,

                ValidAudience =
                    builder.Configuration["Jwt:Audience"],

                ValidateLifetime = true,

                ClockSkew = TimeSpan.Zero
            };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken =
                    context.Request.Query["access_token"];

                var path =
                    context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Enter your JWT token."
        });

    options.AddSecurityRequirement(document =>
        new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference(
                "Bearer",
                document)] = []
        });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.MapHub<ChatHub>("/hubs/chat");

app.Run();