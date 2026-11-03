using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.Sqlite;
using Wolfden.Index;

namespace Wolfden.Api;

/// <summary>The den's errors, from <c>GET /api/den/errors</c> (#280).</summary>
/// <param name="Version">The shape of this result.</param>
/// <param name="Count">How many errors the den has, or a pinned server's universe and the den-level files.</param>
/// <param name="Truncated">True if <paramref name="Errors"/> stops short of <paramref name="Count"/>.</param>
/// <param name="DenLoaded">False if <c>den.yaml</c> doesn't load, so nothing else was read.</param>
/// <param name="Errors">The errors, by path and line.</param>
public sealed record DenErrorsResult(int Version, int Count, bool Truncated, bool DenLoaded, IReadOnlyList<DenErrorView> Errors);

/// <summary>One error in <see cref="DenErrorsResult"/>.</summary>
/// <param name="Path">The file or folder.</param>
/// <param name="Universe">The universe it's in, or null for a den-level file.</param>
/// <param name="Name">The note's name, if the index has it.</param>
/// <param name="Kind">What the path is: <c>note</c>, <c>outline</c>, <c>book</c>, <c>series</c>, <c>universe</c>, <c>den</c>, <c>pack</c>, <c>folder</c>, or <c>other</c>.</param>
/// <param name="Line">The line, or null.</param>
/// <param name="Rule">The rule.</param>
/// <param name="Message">The message.</param>
/// <param name="Reach">What it leaves out: <c>section</c>, <c>file</c>, <c>container</c>, or <c>den</c>.</param>
/// <param name="Section">For a section, its name.</param>
/// <param name="SectionLine">For a section, its heading's line.</param>
/// <param name="CanEdit">True if Edit file can open it here (<c>/api/file/source</c>).</param>
public sealed record DenErrorView(
    string Path, string? Universe, string? Name, string Kind, int? Line, string Rule, string Message, string Reach, string? Section, int? SectionLine, bool CanEdit);

/// <summary>
/// The den's errors and fixing them from the web UI (#280). These aren't a
/// view at a story position: they take no position, and refuse one, so a page
/// at a position can't ask for less or be told it got a safe answer. The list
/// is the writer's, like editing, which shows everything.
/// </summary>
internal static class FileEndpoints
{
    /// <summary>The most errors the list holds; <see cref="DenErrorsResult.Count"/> still counts them all.</summary>
    private const int ListedErrors = 500;

    /// <summary>The paths under <c>/api</c> that answer GET here.</summary>
    public static readonly IReadOnlyList<string> GetPaths = ["den/errors", "file/source"];

    /// <summary>The paths under <c>/api</c> that answer POST here.</summary>
    public static readonly IReadOnlyList<string> PostPaths = ["file/edit"];

    /// <summary>Maps the endpoints onto <paramref name="api"/>, through <paramref name="tools"/>, so a save takes its turn with the other writes.</summary>
    public static void Map(RouteGroupBuilder api, DenTools tools)
    {
        api.MapGet("/den/errors", (HttpContext c) => ReadEndpoints.Answer(c, [], _ => Errors(tools)));
        api.MapGet("/file/source", (HttpContext c) => ReadEndpoints.Answer(c, ["path"], q => tools.GetFileSource(q.Text("path") ?? "")));
        api.MapPost("/file/edit", (Func<HttpContext, Task<IResult>>)(c => Edit(c, tools)));
    }

    private static DenErrorsResult Errors(DenTools tools)
    {
        DenErrorList list;
        try
        {
            list = tools.CurrentErrors();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or SqliteException)
        {
            throw new DenToolException(ToolErrorCodes.DenUnreadable, $"Couldn't read or rebuild the den's index: {e.Message}");
        }

        return new DenErrorsResult(
            ReadEndpoints.ResultVersion, list.Errors.Count, list.Errors.Count > ListedErrors, list.DenLoaded,
            list.Errors.Take(ListedErrors).Select(e =>
            {
                var kind = DenErrors.KindOf(e.Path);
                return new DenErrorView(
                    e.Path, DenErrors.UniverseOf(e.Path), list.Names.GetValueOrDefault(e.Path), kind, e.Line, e.Rule, e.Message, e.Reach.ToString().ToLowerInvariant(),
                    e.Section, e.SectionLine, DenErrors.CanEdit(kind, tools.PinnedUniverse));
            }).ToList());
    }

    /// <summary>Saves a file by its path. A <c>stale_file</c> error carries the file as it is now.</summary>
    private static async Task<IResult> Edit(HttpContext context, DenTools tools)
    {
        if (!WriteEndpoints.IsJson(context.Request.ContentType))
        {
            return Results.Json(
                new ApiError("unsupported_media_type", $"'{context.Request.Path}' takes a JSON body: send it with 'Content-Type: application/json'."),
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        string? path = null;
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

            var body = WriteEndpoints.Body.Of(context, json, ["path", "text", "expectedHash"]);
            path = body.Text("path");
            var text = body.Text("text");
            var hash = body.Text("expectedHash");
            using (tools.CancelWith(context.RequestAborted))
            {
                return Results.Json(tools.ReplaceFileAt(path, text, hash));
            }
        }
        catch (DenToolException e) when (e.Code == ToolErrorCodes.StaleFile)
        {
            return Results.Json(new ApiError(e.Code, e.Message, CurrentFile: Current(tools, path!)), statusCode: ReadEndpoints.StatusOf(e.Code));
        }
        catch (DenToolException e)
        {
            return Results.Json(new ApiError(e.Code, e.Message), statusCode: ReadEndpoints.StatusOf(e.Code));
        }
    }

    private static FileSourceResult? Current(DenTools tools, string path)
    {
        try
        {
            return tools.GetFileSource(path);
        }
        catch (DenToolException)
        {
            return null;
        }
    }
}
