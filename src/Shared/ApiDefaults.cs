using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

internal static class ApiDefaults
{
    public static void AddApiDefaults(this WebApplicationBuilder builder, string keySetting)
    {
        var key = builder.Configuration[keySetting];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32)
            throw new InvalidOperationException($"{keySetting} must contain at least 32 characters");
        builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? context.HttpContext.TraceIdentifier);
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddFixedWindowLimiter("api", limiter =>
            {
                limiter.PermitLimit = 120;
                limiter.Window = TimeSpan.FromMinutes(1);
                limiter.QueueLimit = 0;
            });
        });
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16_384);
    }

    public static void UseApiDefaults(this WebApplication app, string keySetting)
    {
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();
        var expected = SHA256.HashData(Encoding.UTF8.GetBytes(app.Configuration[keySetting]!));
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health/live" || context.Request.Path == "/health/ready")
            {
                await next(context);
                return;
            }
            var supplied = context.Request.Headers["X-Api-Key"];
            if (supplied.Count != 1 || !CryptographicOperations.FixedTimeEquals(expected,
                    SHA256.HashData(Encoding.UTF8.GetBytes(supplied.ToString()))))
            {
                await Results.Problem(statusCode: 401, title: "Valid API key required").ExecuteAsync(context);
                return;
            }
            await next(context);
        });
        app.MapGet("/health/ready", async (NpgsqlDataSource db, CancellationToken ct) =>
        {
            try
            {
                await using var command = db.CreateCommand("SELECT 1");
                await command.ExecuteScalarAsync(ct);
                return Results.Ok(new { status = "ready" });
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                return Results.Problem(statusCode: 503, title: "Database unavailable");
            }
        });
    }
}
