using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Net.Http.Headers;

namespace Wolfden.Api;

/// <summary>
/// Adding an image to a note from the editor (#187; design.md §9):
/// <c>POST /api/entity/image?entity=&amp;universe=&amp;name=</c>, with the
/// image's bytes as the body (<see cref="DenTools.AddImage"/>).
/// </summary>
/// <remarks>
/// <para>
/// The body is the file itself, not JSON or a form, sent as
/// <c>application/octet-stream</c> or an <c>image/...</c> type; anything
/// else is 415. A plain HTML form can send neither, so, as for the JSON
/// edits, a script on another site would need a CORS preflight, which the
/// server never gives, and the cross-site guard refuses its <c>Origin</c>
/// anyway. <c>name</c> is the file's name as the writer had it; the server
/// makes the saved name from it, so the client never chooses a path.
/// </para>
/// <para>
/// A body larger than <see cref="DenTools.MaxImageBytes"/> is 413
/// <c>too_large</c>, read no further. The answer is
/// <see cref="AddImageResult"/>, with 201.
/// </para>
/// </remarks>
public static class ImageEndpoints
{
    /// <summary>The paths under <c>/api</c> that answer POST.</summary>
    public static readonly IReadOnlyList<string> PostPaths = ["entity/image"];

    /// <summary>Maps the endpoint onto <paramref name="api"/>, writing through <paramref name="tools"/>, the server's one set of write tools.</summary>
    internal static void Map(RouteGroupBuilder api, DenTools tools) =>
        api.MapPost("/entity/image", (Func<HttpContext, Task<IResult>>)(c => Add(c, tools)));

    private static async Task<IResult> Add(HttpContext context, DenTools tools)
    {
        if (!IsImageBody(context.Request.ContentType))
        {
            return Results.Json(
                new ApiError(
                    "unsupported_media_type",
                    $"'{context.Request.Path}' takes the image file itself as the body: send it with 'Content-Type: application/octet-stream' or its image type."),
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        try
        {
            var query = ReadEndpoints.ApiQuery.Of(context, ["entity", "universe", "name"]);
            var entity = query.Text("entity");
            DenReader.CheckEntityName(entity);
            if (context.Request.ContentLength > DenTools.MaxImageBytes)
            {
                return TooLarge();
            }

            var bytes = await ReadBody(context.Request.Body, DenTools.MaxImageBytes, context.RequestAborted).ConfigureAwait(false);
            if (bytes is null)
            {
                return TooLarge();
            }

            var result = tools.AddImage(entity!, query.Text("name"), bytes, query.Text("universe"));
            return Results.Json(result, statusCode: StatusCodes.Status201Created);
        }
        catch (DenToolException e)
        {
            return Results.Json(new ApiError(e.Code, e.Message, e.Candidates), statusCode: ReadEndpoints.StatusOf(e.Code));
        }
    }

    private static IResult TooLarge() => Results.Json(
        new ApiError(
            "too_large",
            $"Wolfden adds images of up to {DenTools.MaxImageBytes / (1024 * 1024)} MB. Make it smaller in an image editor, then add it again."),
        statusCode: StatusCodes.Status413PayloadTooLarge);

    /// <summary>The body's bytes, or null if there are more than <paramref name="limit"/>.</summary>
    private static async Task<byte[]?> ReadBody(Stream body, int limit, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(chunk, token).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > limit)
            {
                return null;
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    /// <summary>True for <c>application/octet-stream</c> or an <c>image/...</c> type, with or without parameters.</summary>
    private static bool IsImageBody(string? contentType) =>
        MediaTypeHeaderValue.TryParse(contentType, out var type)
        && (type.MediaType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase)
            || type.MediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase));
}
