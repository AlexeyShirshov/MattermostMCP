using System.Security.Claims;
using MattermostMCP.Auth;
using MattermostMCP.Handlers;
using MattermostMCP.MmDesktop;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace MattermostMCP;

public sealed class Startup(IConfiguration configuration, IWebHostEnvironment environment)
{
    public const string AUTHENTICATED_USER_POLICY = "AuthenticatedUser";

    private readonly IConfiguration _configuration = configuration;
    private readonly IWebHostEnvironment _environment = environment;
    private string[]? _allowedOrigins;

    public void ConfigureServices(IServiceCollection services)
    {
        var authOptions = _configuration.GetSection(AuthOptions.SectionName)
            .Get<AuthOptions>() ?? new AuthOptions();
        var mmDesktopOptions = _configuration.GetSection(MmDesktopOptions.SectionName)
            .Get<MmDesktopOptions>() ?? new MmDesktopOptions();

        services.Configure<AuthOptions>(_configuration.GetSection(AuthOptions.SectionName));
        services.Configure<MmDesktopOptions>(
            _configuration.GetSection(MmDesktopOptions.SectionName));

        services.AddProblemDetails();

        var baseAddress = new Uri(mmDesktopOptions.BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        var timeout = TimeSpan.FromSeconds(mmDesktopOptions.TimeoutSeconds);

        // Только для логина — без auth-хендлера, чтобы не уйти в рекурсию.
        services.AddHttpClient(MattermostTokenProvider.AuthClientName, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = timeout;
        });

        services.AddSingleton<MattermostTokenProvider>();
        services.AddTransient<MattermostAuthHandler>();

        services
            .AddHttpClient<MattermostClient>(client =>
            {
                client.BaseAddress = baseAddress;
                client.Timeout = timeout;
            })
            .AddHttpMessageHandler<MattermostAuthHandler>()
            .SetHandlerLifetime(TimeSpan.FromMinutes(5));

        services.AddScoped<ThreadSearchService>();
        services.AddScoped<ToolsHandler>();
        services.AddScoped<McpServer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = authOptions.Authority;
                if (!string.IsNullOrWhiteSpace(authOptions.MetadataAddress))
                {
                    options.MetadataAddress = authOptions.MetadataAddress;
                }

                options.Audience = authOptions.Audience;
                options.RequireHttpsMetadata = authOptions.RequireHttpsMetadata;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = !string.IsNullOrWhiteSpace(authOptions.Issuer),
                    ValidIssuer = authOptions.Issuer,
                    NameClaimType = "preferred_username",
                    RoleClaimType = ClaimTypes.Role
                };
            });

        services.AddAuthorization(static options =>
            options.AddPolicy(
                AUTHENTICATED_USER_POLICY,
                static policy => policy.RequireAuthenticatedUser()));

        // CORS: по умолчанию разрешаем только loopback-origin'ы (защита от DNS-rebinding),
        // конкретный список можно задать через Cors:AllowedOrigins.
        _allowedOrigins = _configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        services.AddCors(options => options.AddDefaultPolicy(policy =>
        {
            if (_allowedOrigins is { Length: > 0 })
            {
                policy.WithOrigins(_allowedOrigins);
            }
            else
            {
                policy.SetIsOriginAllowed(static origin =>
                    Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback);
            }

            policy.AllowAnyHeader().AllowAnyMethod();
        }));
    }

    public void Configure(WebApplication app)
    {
        if (_environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        // Защита от DNS-rebinding: если Origin передан и он не разрешён — отклоняем.
        // Запросы без Origin (curl, MCP-клиенты) пропускаются.
        app.Use(async (context, next) =>
        {
            var origin = context.Request.Headers.Origin.ToString();
            if (!string.IsNullOrEmpty(origin) && !IsOriginAllowed(origin))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden origin.", context.RequestAborted)
                    .ConfigureAwait(false);
                return;
            }

            await next(context).ConfigureAwait(false);
        });

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapGet(
            "/",
            static () => Results.Ok(new { status = "ok", server = "mattermost-mcp-server" }));

        app.MapThreadEndpoints();
        app.MapMcpEndpoints(_environment);
    }

    private bool IsOriginAllowed(string origin)
    {
        if (_allowedOrigins is { Length: > 0 })
        {
            return Array.Exists(
                _allowedOrigins,
                allowed => string.Equals(allowed, origin, StringComparison.OrdinalIgnoreCase));
        }

        return Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.IsLoopback;
    }
}
