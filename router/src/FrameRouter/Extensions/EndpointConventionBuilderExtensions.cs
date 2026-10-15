using System.Security.Cryptography;
using System.Text;

namespace FrameRouter.Extensions;

internal static class EndpointConventionBuilderExtensions
{
    // Same check on frame.vicio.ovh and frame.home.vicio.ovh: neither host puts its own auth in front of the router.
    public static TBuilder RequireBearer<TBuilder>(this TBuilder builder, string token, string settingName)
        where TBuilder : IEndpointConventionBuilder
    {
        var expected = Encoding.UTF8.GetBytes(token);
        return builder.AddEndpointFilter((context, next) =>
        {
            if (expected.Length == 0)
                return ValueTask.FromResult<object?>(Results.Problem($"This endpoint is off: {settingName} is not set.", statusCode: StatusCodes.Status403Forbidden));

            const string scheme = "Bearer ";
            var header = context.HttpContext.Request.Headers.Authorization.ToString();
            var authorized = header.StartsWith(scheme, StringComparison.Ordinal)
                && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(header[scheme.Length..]), expected);
            return authorized ? next(context) : ValueTask.FromResult<object?>(Results.Unauthorized());
        });
    }
}
