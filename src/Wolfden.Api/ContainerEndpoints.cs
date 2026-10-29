using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Wolfden.Api;

/// <summary>
/// Creating series and books from the web UI (#213; design.md §9):
/// <c>GET /api/works</c> lists the works a new one can be placed after, with
/// the hash of the file that lists them, and <c>POST /api/series/create</c>
/// and <c>POST /api/book/create</c> make one
/// (<see cref="DenTools.CreateSeries"/>, <see cref="DenTools.CreateBook"/>).
/// </summary>
/// <remarks>
/// <para>
/// <c>/api/works?universe=&lt;key&gt;</c> gives the universe's series and
/// standalone books in story order, and with <c>&amp;series=&lt;key&gt;</c>
/// that series' books, with the hash of <c>universe.md</c> or
/// <c>series.md</c>.
/// </para>
/// <para>
/// A create takes a JSON body, sent as <c>application/json</c>, as the
/// editing endpoints do: <c>{name, universe, after, first, expectedHash}</c> for a
/// series, and <c>{name, universe, series, after, first, chapters, expectedHash}</c>
/// for a book (a standalone book without <c>series</c>). <c>first: true</c>
/// places it at the start of the story order (not with <c>after</c>), and
/// neither places it at the end. <c>expectedHash</c>
/// is the hash <c>/api/works</c> gave: the create edits that file's list, so
/// it's refused with <c>stale_file</c> if the file has changed since. It
/// writes through the server's one set of write tools, so it runs one write
/// at a time and lands in the server's checkpoint ledger, and on a server
/// pinned to a universe, it's refused in any other. It answers 201 with
/// what was made: its kind and key, which the page navigates to, its file,
/// and every file written.
/// </para>
/// </remarks>
public static class ContainerEndpoints
{
    /// <summary>The paths under <c>/api</c> that answer GET.</summary>
    public static readonly IReadOnlyList<string> GetPaths = ["works"];

    /// <summary>The paths under <c>/api</c> that answer POST.</summary>
    public static readonly IReadOnlyList<string> PostPaths = ["series/create", "book/create"];

    /// <summary>Maps the endpoints onto <paramref name="api"/>, writing through <paramref name="tools"/>, the server's one set of write tools.</summary>
    internal static void Map(RouteGroupBuilder api, DenTools tools)
    {
        api.MapGet("/works", (HttpContext c) => ReadEndpoints.Answer(c, ["universe", "series"], q => tools.GetWorks(q.Text("universe"), q.Text("series"))));
        api.MapPost("/series/create", (Func<HttpContext, Task<IResult>>)(c => Create(c, ["name", "universe", "after", "first", "expectedHash"], body =>
            tools.CreateSeries(Name(body, "series"), body.OptionalText("universe"), body.OptionalText("after"), Hash(body), body.OptionalBool("first") ?? false))));
        api.MapPost("/book/create", (Func<HttpContext, Task<IResult>>)(c => Create(c, ["name", "universe", "series", "after", "first", "chapters", "expectedHash"], body =>
            tools.CreateBook(
                Name(body, "book"), body.OptionalText("universe"), body.OptionalText("series"), body.OptionalText("after"), body.OptionalInt("chapters"), Hash(body),
                body.OptionalBool("first") ?? false))));
    }

    private static string Name(WriteEndpoints.Body body, string what) =>
        body.RawText("name") ?? throw new DenToolException(ToolErrorCodes.BadArgument, $"Give the new {what} a name.");

    private static string Hash(WriteEndpoints.Body body) => body.OptionalText("expectedHash") ?? throw new DenToolException(
        ToolErrorCodes.BadArgument,
        "Give 'expectedHash': the hash of the universe.md or series.md that lists the works, from /api/works. The create is refused if the file has changed since.");

    private static async Task<IResult> Create(HttpContext context, string[] members, Func<WriteEndpoints.Body, ContainerCreateResult> create)
    {
        if (!context.Request.HasJsonContentType())
        {
            return Results.Json(
                new ApiError("unsupported_media_type", $"'{context.Request.Path}' takes a JSON body: send it with 'Content-Type: application/json'."),
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        try
        {
            if (context.Request.Query.Count > 0)
            {
                throw new DenToolException(ToolErrorCodes.BadArgument, $"'{context.Request.Path}' takes its arguments in the JSON body, not the query.");
            }

            JsonElement json;
            try
            {
                using var document = await JsonDocument.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                json = document.RootElement.Clone();
            }
            catch (JsonException e)
            {
                throw new DenToolException(ToolErrorCodes.BadArgument, $"The body isn't JSON: {e.Message}");
            }

            return Results.Json(create(WriteEndpoints.Body.Of(context, json, members)), statusCode: StatusCodes.Status201Created);
        }
        catch (DenToolException e)
        {
            return Results.Json(new ApiError(e.Code, e.Message, e.Candidates), statusCode: ReadEndpoints.StatusOf(e.Code));
        }
    }
}
