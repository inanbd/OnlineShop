using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OnlineShop.Application.Abstractions;
using OnlineShop.Application.Abstractions.Persistence;
using OnlineShop.Domain.Common;
using OnlineShop.Persistence.Repositories;

namespace OnlineShop.Api;

public static class ExceptionHandling
{
    /// <summary>
    /// Maps application and persistence failures onto HTTP status codes.
    /// </summary>
    public static void UseOnlineShopExceptionHandler(this WebApplication app)
    {
        app.UseExceptionHandler(builder => builder.Run(async context =>
        {
            var feature = context.Features.Get<IExceptionHandlerFeature>();
            var exception = feature?.Error;

            if (exception is null)
            {
                return;
            }

            var problem = Describe(exception, app.Environment.IsDevelopment());

            var logger = context.RequestServices
                .GetRequiredService<ILoggerFactory>()
                .CreateLogger("OnlineShop.Api.ExceptionHandling");

            if (problem.Status >= StatusCodes.Status500InternalServerError)
            {
                logger.LogError(exception, "Unhandled failure on {Method} {Path}.",
                    context.Request.Method, context.Request.Path);
            }
            else
            {
                logger.LogInformation("{Method} {Path} rejected: {Detail}",
                    context.Request.Method, context.Request.Path, problem.Detail);
            }

            context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsJsonAsync(problem);
        }));
    }

    private static ProblemDetails Describe(Exception exception, bool isDevelopment)
    {
        return exception switch
        {
            NotFoundException notFound => new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Not found",
                Detail = notFound.Message,
            },

            // Domain rule violations are the caller's problem, not the server's.
            DomainException domain => new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Request rejected",
                Detail = domain.Message,
            },

            // A concurrent writer got there first. Retrying is reasonable.
            DbConcurrencyException concurrency => new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Conflict",
                Detail = concurrency.Message,
            },

            // Reaching for the wrong database is a bug in the handler, so it is
            // a 500. The message is only echoed outside production, where it is
            // useful to whoever is fixing it.
            CqrsConnectionViolationException violation => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "CQRS connection rule violated",
                Detail = isDevelopment ? violation.Message : "An internal error occurred.",
            },

            _ => new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "Unexpected error",
                Detail = isDevelopment ? exception.Message : "An internal error occurred.",
            },
        };
    }
}
