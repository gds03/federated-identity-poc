using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Federated.Common;

public static class CommonConstants
{
    public const string CorrelationIdHeader = "X-Correlation-Id";
}

public class TransactionIdMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TransactionIdMiddleware> _logger;

    public TransactionIdMiddleware(RequestDelegate next, ILogger<TransactionIdMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId = context.Request.Headers[CommonConstants.CorrelationIdHeader].ToString();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Response.Headers[CommonConstants.CorrelationIdHeader] = correlationId;
        context.Request.Headers[CommonConstants.CorrelationIdHeader] = correlationId;
        context.Items[CommonConstants.CorrelationIdHeader] = correlationId;

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["CorrelationId"] = correlationId
        }))
        {
            await _next(context);
        }
    }
}

public static class TransactionIdMiddlewareExtensions
{
    public static IApplicationBuilder UseTransactionId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<TransactionIdMiddleware>();
    }
}
