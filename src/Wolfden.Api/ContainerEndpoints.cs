using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Wolfden.Api;

/// <summary>
/// Creating series and books (#213) and adding chapters (#215) from the web UI (design.md §9):
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
/// <para>
/// Adding a chapter (#215): <c>GET /api/book/chapters?book=&lt;key&gt;&amp;universe=&lt;key&gt;</c>
/// gives a book's chapters (key, number, title) with the hash of its
/// <c>book.md</c> and whether it's final, as the MCP tool <c>get_chapter</c>
/// does, and <c>POST /api/chapter/create</c> with
/// <c>{book, universe, title, after, expectedHash}</c> adds one, at the end
/// or after the chapter <c>after</c> (<see cref="DenTools.AddChapter"/>),
/// answering 201 with its key, number, and the numbers it changed. A final
/// book refuses it (<c>frozen</c>), and a stale <c>book.md</c> too
/// (<c>stale_file</c>). It may give the chapter its own chapter fields in
/// <c>fields</c>, an object from field name to value (#225).
/// </para>
/// <para>
/// Chapter fields (#225): <c>GET /api/book/chapter-fields?book=&lt;key&gt;&amp;universe=&lt;key&gt;</c>
/// gives each chapter's fields, as written and as they hold, with the den's
/// chapter field definitions and the hash of <c>book.md</c>
/// (<see cref="DenTools.GetBookChapterFields"/>), and <c>POST /api/chapter/edit</c>
/// with <c>{book, universe, chapter, field, changes, expectedHash}</c> changes one
/// field of one chapter (<see cref="DenTools.UpdateChapterFields"/>, the MCP
/// <c>update_chapter_fields</c>). <c>GET /api/chapter-defaults?universe=&amp;series=&amp;book=</c>
/// gives a container's <c>chapter_defaults</c> the same way
/// (<see cref="DenTools.GetChapterDefaultsSource"/>), and
/// <c>POST /api/chapter-defaults/edit</c> with
/// <c>{universe, series, book, field, changes, expectedHash}</c> changes one of
/// them (<see cref="DenTools.UpdateChapterDefaults"/>). Each change is
/// <c>{op, value, index, item}</c>, with <c>op</c> one of <c>set</c>,
/// <c>add</c>, <c>remove</c>, and <c>clear</c>, as the MCP tools take them, and
/// a plain number as <c>{"$number": "2.50"}</c>. A final book, a stale file,
/// and an edit of a series' or universe's defaults that would change a final
/// book's chapters are refused, as for the MCP tools.
/// </para>
/// </remarks>
public static class ContainerEndpoints
{
    /// <summary>The paths under <c>/api</c> that answer GET.</summary>
    public static readonly IReadOnlyList<string> GetPaths = ["works", "book/chapters", "book/chapter-fields", "chapter-defaults"];

    /// <summary>The paths under <c>/api</c> that answer POST.</summary>
    public static readonly IReadOnlyList<string> PostPaths = ["series/create", "book/create", "chapter/create", "chapter/edit", "chapter-defaults/edit"];

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
        api.MapGet("/book/chapters", (HttpContext c) => ReadEndpoints.Answer(
            c, ["book", "universe"], q => tools.GetChapter(q.Text("book") ?? "", null, q.Text("universe"))));
        api.MapPost("/chapter/create", (Func<HttpContext, Task<IResult>>)(c => Create(c, ["book", "universe", "title", "after", "fields", "expectedHash"], body =>
            tools.AddChapter(
                body.OptionalText("book") ?? throw new DenToolException(ToolErrorCodes.BadArgument, "Give the key of the book the chapter goes in, in 'book'."),
                body.RawText("title"),
                body.OptionalText("after"),
                body.OptionalText("expectedHash") ?? throw new DenToolException(
                    ToolErrorCodes.BadArgument,
                    "Give 'expectedHash': the hash of the book's book.md, from /api/book/chapters. The add is refused if the file has changed since."),
                body.OptionalText("universe"),
                body.OptionalJson("fields")))));

        api.MapGet("/book/chapter-fields", (HttpContext c) => ReadEndpoints.Answer(
            c, ["book", "universe"], q => tools.GetBookChapterFields(q.Text("book") ?? "", q.Text("universe"))));
        api.MapPost("/chapter/edit", (Func<HttpContext, Task<IResult>>)(c => Create(
            c, ["book", "universe", "chapter", "field", "changes", "expectedHash"], body => tools.UpdateChapterFields(
                body.OptionalText("book") ?? throw new DenToolException(ToolErrorCodes.BadArgument, "Give the key of the chapter's book, in 'book'."),
                body.OptionalText("chapter") ?? throw new DenToolException(ToolErrorCodes.BadArgument, "Give the chapter's key, in 'chapter'."),
                EditHash(body, "/api/book/chapter-fields"),
                ChapterChanges(body),
                body.OptionalText("universe"),
                numbersAsText: true),
            StatusCodes.Status200OK)));
        api.MapGet("/chapter-defaults", (HttpContext c) => ReadEndpoints.Answer(
            c, ["universe", "series", "book"], q => tools.GetChapterDefaultsSource(q.Text("universe"), q.Text("series"), q.Text("book"))));
        api.MapPost("/chapter-defaults/edit", (Func<HttpContext, Task<IResult>>)(c => Create(
            c, ["universe", "series", "book", "field", "changes", "expectedHash"], body => tools.UpdateChapterDefaults(
                EditHash(body, "/api/chapter-defaults"),
                ChapterChanges(body),
                body.OptionalText("universe"),
                body.OptionalText("series"),
                body.OptionalText("book"),
                numbersAsText: true),
            StatusCodes.Status200OK)));
    }

    /// <summary>The members a change of a chapter field or default takes.</summary>
    private static readonly string[] ChangeMembers = ["op", "value", "index", "item"];

    private static string EditHash(WriteEndpoints.Body body, string from) => body.OptionalText("expectedHash") ?? throw new DenToolException(
        ToolErrorCodes.BadArgument, $"Give 'expectedHash': the hash of the file, from {from}. The edit is refused if the file has changed since.");

    /// <summary>
    /// The changes of one chapter field (<c>field</c>), for the write tools:
    /// each change is <c>{op, value, index, item}</c>, and gets the field, so
    /// an edit can't reach another field.
    /// </summary>
    private static JsonElement ChapterChanges(WriteEndpoints.Body body)
    {
        var field = body.OptionalText("field") ?? throw new DenToolException(ToolErrorCodes.BadArgument, "Give the chapter field to change, in 'field', such as 'pov'.");
        var changes = body.Json("changes");
        if (changes.ValueKind != JsonValueKind.Array || changes.GetArrayLength() == 0)
        {
            throw new DenToolException(ToolErrorCodes.BadArgument, "'changes' is a list of changes, such as [{\"op\": \"set\", \"value\": \"present\"}].");
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var change in changes.EnumerateArray())
        {
            if (change.ValueKind != JsonValueKind.Object)
            {
                throw new DenToolException(ToolErrorCodes.BadArgument, "Each change is an object: {op, value, index, item}.");
            }

            var copy = new Dictionary<string, object?>(StringComparer.Ordinal) { ["field"] = field };
            foreach (var member in change.EnumerateObject())
            {
                if (!ChangeMembers.Contains(member.Name, StringComparer.Ordinal))
                {
                    throw new DenToolException(ToolErrorCodes.BadArgument, $"A change doesn't take '{member.Name}'. It takes: {string.Join(", ", ChangeMembers)}.");
                }

                if (!copy.TryAdd(member.Name, member.Value))
                {
                    throw new DenToolException(ToolErrorCodes.BadArgument, $"Give '{member.Name}' once in a change.");
                }
            }

            result.Add(copy);
        }

        return JsonSerializer.SerializeToElement(result);
    }

    private static string Name(WriteEndpoints.Body body, string what) =>
        body.RawText("name") ?? throw new DenToolException(ToolErrorCodes.BadArgument, $"Give the new {what} a name.");

    private static string Hash(WriteEndpoints.Body body) => body.OptionalText("expectedHash") ?? throw new DenToolException(
        ToolErrorCodes.BadArgument,
        "Give 'expectedHash': the hash of the universe.md or series.md that lists the works, from /api/works. The create is refused if the file has changed since.");

    private static async Task<IResult> Create<T>(HttpContext context, string[] members, Func<WriteEndpoints.Body, T> create, int status = StatusCodes.Status201Created)
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

            return Results.Json(create(WriteEndpoints.Body.Of(context, json, members)), statusCode: status);
        }
        catch (DenToolException e)
        {
            return Results.Json(new ApiError(e.Code, e.Message, e.Candidates), statusCode: ReadEndpoints.StatusOf(e.Code));
        }
    }
}
