using System.CommandLine;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Spectre.Console;

namespace DeveloperCli.Commands;

public sealed class ClaudeUsageCommand : Command
{
    private const long LargeContextThreshold = 200_000;
    private const int LargestResultCount = 20;

    private static readonly string[] GapBucketNames = ["First call", "Under 60 s", "1 to 5 min", "5 to 60 min", "Over 60 min"];

    /// List prices per million tokens. Cache write is 1.25x input for the 5-minute TTL and 2x input for the
    /// 1-hour TTL; cache read is 0.1x input, except Claude Fable 5.1 which reads at a flat $0.25 per million.
    /// Keys are matched as prefixes so dated model ids (claude-haiku-4-5-20251001) resolve to their family.
    private static readonly Dictionary<string, ModelPrice> ListPrices = new()
    {
        ["claude-fable-5-1"] = new ModelPrice(10.00m, 12.50m, 20.00m, 0.25m, 50.00m),
        ["claude-opus-5"] = new ModelPrice(5.00m, 6.25m, 10.00m, 0.50m, 25.00m),
        ["claude-sonnet-5"] = new ModelPrice(2.00m, 2.50m, 4.00m, 0.20m, 10.00m),
        ["claude-haiku-4-5"] = new ModelPrice(1.00m, 1.25m, 2.00m, 0.10m, 5.00m)
    };

    public ClaudeUsageCommand() : base("claude-usage", "Report Claude Code token usage and list-price cost from local session transcripts")
    {
        var sinceOption = new Option<string?>("--since") { Description = "Only include calls on or after this date (yyyy-MM-dd, UTC)" };
        var untilOption = new Option<string?>("--until") { Description = "Only include calls on or before this date (yyyy-MM-dd, UTC)" };
        var projectOption = new Option<string?>("--project", "-p") { Description = "Limit to one project, given as an encoded transcript folder name or a working directory path" };
        var jsonOption = new Option<string?>("--json") { Description = "Write the raw aggregates to this file" };
        var diffOption = new Option<string?>("--diff") { Description = "Compare against a previously written aggregates file and add a delta column" };
        var quietOption = new Option<bool>("--quiet", "-q") { Description = "Print only the report, so the output can be redirected to a Markdown file" };

        Options.Add(sinceOption);
        Options.Add(untilOption);
        Options.Add(projectOption);
        Options.Add(jsonOption);
        Options.Add(diffOption);
        Options.Add(quietOption);

        SetAction(parseResult => Execute(
                parseResult.GetValue(sinceOption),
                parseResult.GetValue(untilOption),
                parseResult.GetValue(projectOption),
                parseResult.GetValue(jsonOption),
                parseResult.GetValue(diffOption),
                parseResult.GetValue(quietOption)
            )
        );
    }

    private static void Execute(string? since, string? until, string? project, string? jsonFile, string? diffFile, bool quiet)
    {
        // Spectre re-wraps written text at the console width, which would corrupt wide Markdown table rows.
        AnsiConsole.Profile.Width = Math.Max(AnsiConsole.Profile.Width, 500);

        var transcriptFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects");
        if (!Directory.Exists(transcriptFolder))
        {
            AnsiConsole.MarkupLine($"[red]No Claude Code transcripts found at [bold]{transcriptFolder}[/].[/]");
            Environment.Exit(1);
        }

        var sinceParsed = TryParseDate(since, out var sinceDate);
        var untilParsed = TryParseDate(until, out var untilDate);
        if (!sinceParsed || !untilParsed)
        {
            AnsiConsole.MarkupLine("[red]Error: --since and --until must be dates in the form yyyy-MM-dd.[/]");
            Environment.Exit(1);
        }

        UsageReport? baseline = null;
        if (diffFile is not null)
        {
            if (!File.Exists(diffFile))
            {
                AnsiConsole.MarkupLine($"[red]Error: the baseline file [bold]{diffFile}[/] does not exist.[/]");
                Environment.Exit(1);
            }

            try
            {
                baseline = JsonSerializer.Deserialize<UsageReport>(File.ReadAllText(diffFile));
            }
            catch (JsonException exception)
            {
                AnsiConsole.MarkupLine($"[red]Error: the baseline file could not be read. {exception.Message}[/]");
                Environment.Exit(1);
            }

            if (baseline is null)
            {
                AnsiConsole.MarkupLine("[red]Error: the baseline file is empty.[/]");
                Environment.Exit(1);
            }
        }

        var stopwatch = Stopwatch.StartNew();
        var transcripts = FindTranscripts(transcriptFolder, project);
        if (transcripts.Length == 0)
        {
            AnsiConsole.MarkupLine($"[yellow]No transcripts matched. Looked in [bold]{transcriptFolder}[/].[/]");
            Environment.Exit(1);
        }

        var scans = new FileScan[transcripts.Length];
        Parallel.For(0, transcripts.Length, index => scans[index] = ScanTranscript(transcripts[index], sinceDate, untilDate));

        var included = scans.Where(scan => scan.Calls > 0 || scan.ToolCalls.Count > 0).OrderBy(scan => scan.Scope, StringComparer.Ordinal)
            .ThenBy(scan => scan.Session, StringComparer.Ordinal).ThenBy(scan => scan.Agent, StringComparer.Ordinal).ToArray();

        var report = BuildReport(included, since, until, project);

        if (jsonFile is not null)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(jsonFile));
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(jsonFile, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }

        PrintReport(report, baseline);

        if (quiet) return;

        var megabytes = transcripts.Sum(transcript => new FileInfo(transcript.Path).Length) / (1024d * 1024d);
        AnsiConsole.MarkupLine($"\n[green]Scanned {transcripts.Length} transcripts ({megabytes:N0} MB) in {stopwatch.Elapsed.TotalSeconds:N1} s.[/]");
        if (jsonFile is not null) AnsiConsole.MarkupLine($"[green]Aggregates written to [blue]{jsonFile}[/].[/]");
    }

    private static bool TryParseDate(string? value, out DateOnly date)
    {
        date = default;
        if (value is null) return true;
        return DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
    }

    private static Transcript[] FindTranscripts(string transcriptFolder, string? project)
    {
        var encodedProject = project is null ? null : EncodeProject(project);

        var projectFolders = Directory.GetDirectories(transcriptFolder)
            .Where(folder => encodedProject is null || Path.GetFileName(folder) == encodedProject)
            .OrderBy(folder => folder, StringComparer.Ordinal);

        var transcripts = new List<Transcript>();
        foreach (var projectFolder in projectFolders)
        {
            // A session transcript sits directly in the project folder; each of its subagents gets its own file
            // under <session-id>/subagents/. Other folders (tool-results, memory) hold no transcripts.
            foreach (var file in Directory.GetFiles(projectFolder, "*.jsonl").OrderBy(file => file, StringComparer.Ordinal))
            {
                transcripts.Add(new Transcript(file, "Main session", Path.GetFileNameWithoutExtension(file), ""));
            }

            foreach (var sessionFolder in Directory.GetDirectories(projectFolder).OrderBy(folder => folder, StringComparer.Ordinal))
            {
                var subagentFolder = Path.Combine(sessionFolder, "subagents");
                if (!Directory.Exists(subagentFolder)) continue;

                foreach (var file in Directory.GetFiles(subagentFolder, "*.jsonl").OrderBy(file => file, StringComparer.Ordinal))
                {
                    transcripts.Add(new Transcript(file, "Subagent", Path.GetFileName(sessionFolder), Path.GetFileNameWithoutExtension(file)));
                }
            }
        }

        return transcripts.ToArray();
    }

    /// Claude Code encodes the working directory into a folder name by replacing every character that is not a
    /// letter, digit or hyphen with a hyphen. A name that is already encoded passes through unchanged.
    private static string EncodeProject(string project)
    {
        var encoded = new StringBuilder(project.Length);
        foreach (var character in project)
        {
            encoded.Append(char.IsAsciiLetterOrDigit(character) || character == '-' ? character : '-');
        }

        return encoded.ToString();
    }

    private static FileScan ScanTranscript(Transcript transcript, DateOnly since, DateOnly until)
    {
        var scan = new FileScan(transcript.Scope, transcript.Session, transcript.Agent);
        var countedRequests = new HashSet<string>(StringComparer.Ordinal);
        var countedToolUses = new HashSet<string>(StringComparer.Ordinal);
        var countedToolResults = new HashSet<string>(StringComparer.Ordinal);
        var toolNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
        var callState = new CallState();

        using var stream = new FileStream(transcript.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0) continue;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // A transcript that is being written can end in a partial line.
                continue;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                var type = GetString(root, "type");
                if (type is not ("assistant" or "user")) continue;

                if (!root.TryGetProperty("timestamp", out var timestampElement) ||
                    !DateTimeOffset.TryParse(timestampElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var timestamp))
                {
                    continue;
                }

                var date = DateOnly.FromDateTime(timestamp.UtcDateTime);
                if (since != default && date < since) continue;
                if (until != default && date > until) continue;

                if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object) continue;

                if (type == "assistant")
                {
                    var model = GetString(message, "model") ?? "(unknown)";

                    // Synthetic assistant messages are produced locally (interrupts, errors) and bill nothing.
                    if (model != "<synthetic>")
                    {
                        // One API call is written as one line per content block, each carrying the full usage of
                        // that call and all sharing a request id. Only the first line of a call may be counted.
                        var requestId = GetString(root, "requestId") ?? GetString(message, "id");
                        if (requestId is not null && countedRequests.Add(requestId))
                        {
                            CountCall(scan, message, model, timestamp, callState);
                        }
                    }
                }

                if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array) continue;

                foreach (var block in content.EnumerateArray())
                {
                    if (block.ValueKind != JsonValueKind.Object) continue;

                    switch (GetString(block, "type"))
                    {
                        case "tool_use":
                            var toolUseId = GetString(block, "id");
                            var toolName = GetString(block, "name") ?? "(unknown)";
                            if (toolUseId is null) break;
                            toolNamesById[toolUseId] = toolName;
                            callState.ToolUseSincePreviousCall = true;
                            if (countedToolUses.Add(toolUseId)) scan.AddToolCall(toolName);
                            break;

                        case "tool_result":
                            var toolResultId = GetString(block, "tool_use_id");
                            if (toolResultId is null || !countedToolResults.Add(toolResultId)) break;
                            var resultName = toolNamesById.GetValueOrDefault(toolResultId, "(unknown)");
                            var bytes = MeasureResultBytes(block);
                            callState.ResultBytesSincePreviousCall += bytes;
                            scan.AddToolResult(resultName, bytes);
                            scan.LargestResults.Add(new LargeResultRow
                                {
                                    Tool = resultName,
                                    Bytes = bytes,
                                    Session = transcript.Agent.Length == 0 ? transcript.Session : $"{transcript.Session}/{transcript.Agent}",
                                    Timestamp = timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
                                }
                            );
                            break;
                    }
                }
            }
        }

        scan.LargestResults = scan.LargestResults.OrderByDescending(result => result.Bytes).Take(LargestResultCount).ToList();
        return scan;
    }

    private static void CountCall(FileScan scan, JsonElement message, string model, DateTimeOffset timestamp, CallState state)
    {
        var resultBytes = state.ResultBytesSincePreviousCall;
        var afterToolUse = state.ToolUseSincePreviousCall;
        state.ResultBytesSincePreviousCall = 0;
        state.ToolUseSincePreviousCall = false;

        if (!message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return;

        var freshInput = GetLong(usage, "input_tokens");
        var cacheRead = GetLong(usage, "cache_read_input_tokens");
        var cacheWrite = GetLong(usage, "cache_creation_input_tokens");
        var output = GetLong(usage, "output_tokens");

        long cacheWrite5M = 0;
        long cacheWrite1H = 0;
        if (usage.TryGetProperty("cache_creation", out var cacheCreation) && cacheCreation.ValueKind == JsonValueKind.Object)
        {
            cacheWrite5M = GetLong(cacheCreation, "ephemeral_5m_input_tokens");
            cacheWrite1H = GetLong(cacheCreation, "ephemeral_1h_input_tokens");
        }

        // Older transcripts have no per-TTL breakdown; attribute those writes to the 5-minute default.
        if (cacheWrite5M + cacheWrite1H != cacheWrite) cacheWrite5M = cacheWrite - cacheWrite1H;

        var context = freshInput + cacheRead + cacheWrite;

        // A conversation only grows, so the prefix a call can read is everything the previous call read plus
        // everything it wrote. A cache read below that means the cached prefix was invalidated and the part
        // below the divergence point had to be written again; the difference is what the re-write cost. The
        // clamp covers compaction, which is the one event that makes a conversation shrink.
        long rewritten = 0;
        if (state.PreviousPrefix < 0)
        {
            scan.ColdStart += cacheWrite;
        }
        else
        {
            if (cacheRead < state.PreviousPrefix)
            {
                rewritten = Math.Min(state.PreviousPrefix - cacheRead, cacheWrite);
                scan.Rewritten += rewritten;
                scan.ChurnCalls++;
                scan.ChurnResultBytes += resultBytes;
            }

            if (!afterToolUse) scan.TurnBoundaries++;
        }

        state.PreviousPrefix = cacheRead + cacheWrite;

        scan.Calls++;
        scan.FreshInput += freshInput;
        scan.CacheRead += cacheRead;
        scan.CacheWrite += cacheWrite;
        scan.Output += output;
        scan.Contexts.Add(context);
        if (context > LargeContextThreshold) scan.CallsAboveThreshold++;

        var firstTimestamp = timestamp.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        if (scan.FirstTimestamp is null || string.CompareOrdinal(firstTimestamp, scan.FirstTimestamp) < 0) scan.FirstTimestamp = firstTimestamp;

        var modelTotals = scan.Models.TryGetValue(model, out var existing) ? existing : scan.Models[model] = new ModelRow { Model = model };
        modelTotals.Calls++;
        modelTotals.FreshInput += freshInput;
        modelTotals.CacheRead += cacheRead;
        modelTotals.CacheWrite5M += cacheWrite5M;
        modelTotals.CacheWrite1H += cacheWrite1H;
        modelTotals.Output += output;
        modelTotals.Rewritten += rewritten;

        var bucket = state.PreviousCall is null ? GapBucketNames[0] : BucketFor(timestamp - state.PreviousCall.Value);
        var gapTotals = scan.Gaps.TryGetValue(bucket, out var gap) ? gap : scan.Gaps[bucket] = new GapRow { Scope = scan.Scope, Bucket = bucket };
        gapTotals.Calls++;
        gapTotals.CacheWrite += cacheWrite;

        state.PreviousCall = timestamp;
    }

    private static string BucketFor(TimeSpan gap)
    {
        if (gap < TimeSpan.FromSeconds(60)) return GapBucketNames[1];
        if (gap < TimeSpan.FromMinutes(5)) return GapBucketNames[2];
        return gap < TimeSpan.FromMinutes(60) ? GapBucketNames[3] : GapBucketNames[4];
    }

    /// The bytes a tool result adds to the context: the result text itself for a plain string, and for the block
    /// form the text of every text block plus the raw JSON of any other block.
    private static long MeasureResultBytes(JsonElement block)
    {
        if (!block.TryGetProperty("content", out var content)) return 0;

        switch (content.ValueKind)
        {
            case JsonValueKind.String:
                return Encoding.UTF8.GetByteCount(content.GetString() ?? "");
            case JsonValueKind.Array:
                long bytes = 0;
                foreach (var item in content.EnumerateArray())
                {
                    var text = item.ValueKind == JsonValueKind.Object ? GetString(item, "text") : null;
                    bytes += Encoding.UTF8.GetByteCount(text ?? item.GetRawText());
                }

                return bytes;
            default:
                return Encoding.UTF8.GetByteCount(content.GetRawText());
        }
    }

    private static string? GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }

    private static long GetLong(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) ? number : 0;
    }

    private static UsageReport BuildReport(FileScan[] scans, string? since, string? until, string? project)
    {
        var report = new UsageReport
        {
            Since = since,
            Until = until,
            Project = project,
            TranscriptCount = scans.Length
        };

        foreach (var scan in scans)
        {
            var contexts = scan.Contexts.Order().ToArray();
            report.Files.Add(new FileRow
                {
                    Scope = scan.Scope,
                    Session = scan.Session,
                    Agent = scan.Agent,
                    Calls = scan.Calls,
                    CacheRead = scan.CacheRead,
                    CacheWrite = scan.CacheWrite,
                    FreshInput = scan.FreshInput,
                    Output = scan.Output,
                    FirstTimestamp = scan.FirstTimestamp ?? "",
                    MedianContext = Percentile(contexts, 0.5),
                    P90Context = Percentile(contexts, 0.9),
                    LargeContextShare = scan.Calls == 0 ? 0 : Math.Round(100d * scan.CallsAboveThreshold / scan.Calls, 1)
                }
            );
        }

        foreach (var group in scans.SelectMany(scan => scan.Models.Values).GroupBy(model => model.Model, StringComparer.Ordinal))
        {
            var row = new ModelRow { Model = group.Key };
            foreach (var model in group)
            {
                row.Calls += model.Calls;
                row.FreshInput += model.FreshInput;
                row.CacheRead += model.CacheRead;
                row.CacheWrite5M += model.CacheWrite5M;
                row.CacheWrite1H += model.CacheWrite1H;
                row.Output += model.Output;
                row.Rewritten += model.Rewritten;
            }

            row.CostUsd = Math.Round(CostOf(row), 2);
            report.Models.Add(row);
        }

        report.Models = report.Models.OrderByDescending(model => model.CostUsd).ThenBy(model => model.Model, StringComparer.Ordinal).ToList();

        foreach (var scope in new[] { "Main session", "Subagent" })
        {
            foreach (var bucket in GapBucketNames)
            {
                var row = new GapRow { Scope = scope, Bucket = bucket };
                foreach (var gap in scans.Where(scan => scan.Scope == scope).Select(scan => scan.Gaps.GetValueOrDefault(bucket)).OfType<GapRow>())
                {
                    row.Calls += gap.Calls;
                    row.CacheWrite += gap.CacheWrite;
                }

                report.GapBuckets.Add(row);
            }
        }

        foreach (var group in scans.GroupBy(scan => (scan.Scope, Agent: AgentTypeOf(scan.Agent))))
        {
            var row = new ChurnRow { Scope = group.Key.Scope, Agent = group.Key.Agent };
            foreach (var scan in group)
            {
                row.Calls += scan.Calls;
                row.CacheWrite += scan.CacheWrite;
                row.ColdStart += scan.ColdStart;
                row.Rewritten += scan.Rewritten;
                row.ChurnCalls += scan.ChurnCalls;
                row.ChurnResultBytes += scan.ChurnResultBytes;
                row.TurnBoundaries += scan.TurnBoundaries;
            }

            report.CacheWriteAttribution.Add(row);
        }

        report.CacheWriteAttribution = report.CacheWriteAttribution.OrderBy(row => row.Scope, StringComparer.Ordinal)
            .ThenByDescending(row => row.CacheWrite).ThenBy(row => row.Agent, StringComparer.Ordinal).ToList();

        foreach (var group in scans.SelectMany(scan => scan.ToolCalls).GroupBy(tool => tool.Key, StringComparer.Ordinal))
        {
            report.Tools.Add(new ToolRow
                {
                    Tool = group.Key,
                    Calls = group.Sum(tool => tool.Value),
                    ResultBytes = scans.Sum(scan => scan.ToolBytes.GetValueOrDefault(group.Key))
                }
            );
        }

        report.Tools = report.Tools.OrderByDescending(tool => tool.ResultBytes).ThenBy(tool => tool.Tool, StringComparer.Ordinal).ToList();

        report.LargestResults = scans.SelectMany(scan => scan.LargestResults).OrderByDescending(result => result.Bytes)
            .ThenBy(result => result.Timestamp, StringComparer.Ordinal).Take(LargestResultCount).ToList();

        return report;
    }

    /// A subagent transcript is named after its agent id, which is the letter a, then the agent type when the
    /// agent was spawned with one, then a 16-character hexadecimal id: agent-aguardian-34cbfcd807603511 for a
    /// guardian and agent-a0a1c257a5a1917d7 for a one-shot Task subagent. Teams append the task to the type,
    /// as in agent-abackend-reviewer-EP-8-3687c8a8a1c5e1ca, so the type is the leading run of segments that
    /// are all lowercase letters and the rest is identity.
    private static string AgentTypeOf(string agent)
    {
        if (agent.Length == 0) return "-";

        var name = agent.StartsWith("agent-a", StringComparison.Ordinal) ? agent["agent-a".Length..] : agent;
        var type = name.Split('-').TakeWhile(segment => segment.Length > 0 && segment.All(char.IsAsciiLetterLower));

        return string.Join('-', type) is { Length: > 0 } result ? result : "(task)";
    }

    private static long Percentile(long[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static decimal CostOf(ModelRow model)
    {
        var price = PriceFor(model.Model);
        if (price is null) return 0;

        return (model.FreshInput * price.Input
                + model.CacheWrite5M * price.CacheWrite5M
                + model.CacheWrite1H * price.CacheWrite1H
                + model.CacheRead * price.CacheRead
                + model.Output * price.Output) / 1_000_000m;
    }

    private static ModelPrice? PriceFor(string model)
    {
        foreach (var (key, price) in ListPrices.OrderByDescending(price => price.Key.Length))
        {
            if (model.StartsWith(key, StringComparison.Ordinal)) return price;
        }

        return null;
    }

    private static void PrintReport(UsageReport report, UsageReport? baseline)
    {
        var window = report.Since is null && report.Until is null
            ? "all transcripts"
            : $"{report.Since ?? "the first call"} to {report.Until ?? "the last call"}";

        WriteLine($"# Claude Code usage, {window}");
        WriteLine();
        WriteLine($"{report.TranscriptCount} transcripts, {report.Files.Count(file => file.Scope == "Main session")} main sessions and " +
                  $"{report.Files.Count(file => file.Scope == "Subagent")} subagents{(report.Project is null ? "" : $", project {report.Project}")}."
        );
        WriteLine();
        WriteLine("A call is one API request. Claude Code writes one line per content block of an assistant message, each");
        WriteLine("line repeating the full usage of that request, so lines are counted once per request id. Context per call");
        WriteLine("is fresh input plus cache read plus cache write.");

        if (baseline is not null)
        {
            var differences = CountDifferences(report, baseline);
            WriteLine();
            WriteLine(differences == 0
                ? "**Diff: zero deltas.** Every aggregate matches the baseline file."
                : $"**Diff: {differences} aggregates differ from the baseline file.**"
            );
        }

        WriteLine();
        WriteLine("## Per session and subagent");
        WriteLine();
        var fileBaseline = baseline?.Files.ToDictionary(KeyOf, file => file.Calls, StringComparer.Ordinal);
        WriteTable(
            ["Scope", "Session", "Agent", "Calls", "Cache read", "Cache write", "Fresh input", "Output", "First call", "Median context", "p90 context", "Calls over 200k"],
            report.Files.Select(file => new[]
                {
                    file.Scope, file.Session, file.Agent.Length == 0 ? "-" : file.Agent, Number(file.Calls), Number(file.CacheRead),
                    Number(file.CacheWrite), Number(file.FreshInput), Number(file.Output), file.FirstTimestamp,
                    Number(file.MedianContext), Number(file.P90Context), $"{file.LargeContextShare.ToString("N1", CultureInfo.InvariantCulture)} %"
                }
            ),
            baseline is null ? null : report.Files.Select(file => Delta(file.Calls, fileBaseline!.GetValueOrDefault(KeyOf(file), -1))),
            "Δ calls"
        );

        WriteLine();
        WriteLine("## Per model");
        WriteLine();
        var modelBaseline = baseline?.Models.ToDictionary(model => model.Model, model => model.Calls, StringComparer.Ordinal);
        WriteTable(
            ["Model", "Calls", "Fresh input", "Cache write 5 min", "Cache write 1 hour", "Re-written prefix", "Cache read", "Output", "Cost at list price"],
            report.Models.Select(model => new[]
                {
                    model.Model, Number(model.Calls), Number(model.FreshInput), Number(model.CacheWrite5M), Number(model.CacheWrite1H),
                    Number(model.Rewritten), Number(model.CacheRead), Number(model.Output),
                    PriceFor(model.Model) is null ? "unpriced" : $"${model.CostUsd.ToString("N2", CultureInfo.InvariantCulture)}"
                }
            ),
            baseline is null ? null : report.Models.Select(model => Delta(model.Calls, modelBaseline!.GetValueOrDefault(model.Model, -1))),
            "Δ calls"
        );

        WriteLine();
        WriteLine($"Total at list price: ${report.Models.Sum(model => model.CostUsd).ToString("N2", CultureInfo.InvariantCulture)}.");

        WriteLine();
        WriteLine("## Cache write by gap since the previous call");
        WriteLine();
        var gapBaseline = baseline?.GapBuckets.ToDictionary(KeyOf, gap => gap.CacheWrite, StringComparer.Ordinal);
        WriteTable(
            ["Scope", "Gap", "Calls", "Cache write"],
            report.GapBuckets.Select(gap => new[] { gap.Scope, gap.Bucket, Number(gap.Calls), Number(gap.CacheWrite) }),
            baseline is null ? null : report.GapBuckets.Select(gap => Delta(gap.CacheWrite, gapBaseline!.GetValueOrDefault(KeyOf(gap), -1))),
            "Δ cache write"
        );

        WriteLine();
        WriteLine("## Cache write attribution");
        WriteLine();
        WriteLine("Every cache write is one of three things. **Cold start** is the first call of a transcript, which has");
        WriteLine("nothing to read. **Re-written prefix** is the part of an already cached prefix that a later call could no");
        WriteLine("longer read, measured as the previous call's cache read plus cache write minus this call's cache read;");
        WriteLine("it is paid for content the model was already sent. **New material** is the remainder, so the tool");
        WriteLine("results, the model's own output and the reminders that genuinely joined the conversation.");
        WriteLine();
        WriteLine("A **turn boundary** is a call whose previous assistant message asked for no tool, so the turn had ended and");
        WriteLine("a new user or teammate message restarted it. **Result bytes before a churn call** totals the tool result");
        WriteLine("bytes that arrived immediately before the calls that re-wrote a prefix: large ingestion would put the");
        WriteLine("bytes and the cache write in the same place, prefix churn separates them.");
        WriteLine();
        WriteTable(
            ["Scope", "Agent", "Calls", "Cache write", "Cold start", "Re-written prefix", "New material", "Churn calls", "Turn boundaries", "Result bytes before a churn call"],
            report.CacheWriteAttribution.Select(row => new[]
                {
                    row.Scope, row.Agent, Number(row.Calls), Number(row.CacheWrite), Number(row.ColdStart), Number(row.Rewritten),
                    Number(row.NewMaterial), Number(row.ChurnCalls), Number(row.TurnBoundaries), Number(row.ChurnResultBytes)
                }
            ),
            null,
            ""
        );

        WriteLine();
        WriteLine("## Per tool");
        WriteLine();
        WriteLine("Result bytes are the UTF-8 bytes the tool result adds to the context.");
        WriteLine();
        var toolBaseline = baseline?.Tools.ToDictionary(tool => tool.Tool, tool => tool.ResultBytes, StringComparer.Ordinal);
        WriteTable(
            ["Tool", "Calls", "Result bytes"],
            report.Tools.Select(tool => new[] { tool.Tool, Number(tool.Calls), Number(tool.ResultBytes) }),
            baseline is null ? null : report.Tools.Select(tool => Delta(tool.ResultBytes, toolBaseline!.GetValueOrDefault(tool.Tool, -1))),
            "Δ result bytes"
        );

        WriteLine();
        WriteLine($"## The {LargestResultCount} largest single tool results");
        WriteLine();
        WriteTable(
            ["Tool", "Bytes", "Session", "Timestamp"],
            report.LargestResults.Select(result => new[] { result.Tool, Number(result.Bytes), result.Session, result.Timestamp }),
            null,
            ""
        );

        WriteLine();
        WriteLine("## List prices used");
        WriteLine();
        WriteLine("Per million tokens. These are list prices and exclude any long-context premium, any discount and any");
        WriteLine("subscription. A cache write costs 1.25x input on the 5-minute TTL and 2x input on the 1-hour TTL.");
        WriteLine();
        WriteTable(
            ["Model", "Input", "Cache write 5 min", "Cache write 1 hour", "Cache read", "Output"],
            ListPrices.Select(price => new[]
                {
                    price.Key, Price(price.Value.Input), Price(price.Value.CacheWrite5M), Price(price.Value.CacheWrite1H),
                    Price(price.Value.CacheRead), Price(price.Value.Output)
                }
            ),
            null,
            ""
        );
    }

    private static string KeyOf(FileRow file)
    {
        return $"{file.Scope}|{file.Session}|{file.Agent}";
    }

    private static string KeyOf(GapRow gap)
    {
        return $"{gap.Scope}|{gap.Bucket}";
    }

    private static string Delta(long current, long baseline)
    {
        if (baseline < 0) return "new";
        var delta = current - baseline;
        return delta == 0 ? "0" : $"{(delta > 0 ? "+" : "")}{Number(delta)}";
    }

    /// The cache write attribution table is derived from the same per-call numbers the file and model tables
    /// already compare, so it is left out here rather than counting one divergence twice.
    private static int CountDifferences(UsageReport report, UsageReport baseline)
    {
        var differences = 0;

        var files = baseline.Files.ToDictionary(KeyOf, file => file, StringComparer.Ordinal);
        foreach (var file in report.Files)
        {
            if (!files.Remove(KeyOf(file), out var other))
            {
                differences++;
                continue;
            }

            differences += Compare(file.Calls, other.Calls) + Compare(file.CacheRead, other.CacheRead) + Compare(file.CacheWrite, other.CacheWrite)
                           + Compare(file.FreshInput, other.FreshInput) + Compare(file.Output, other.Output)
                           + Compare(file.MedianContext, other.MedianContext) + Compare(file.P90Context, other.P90Context);
        }

        differences += files.Count;

        var models = baseline.Models.ToDictionary(model => model.Model, model => model, StringComparer.Ordinal);
        foreach (var model in report.Models)
        {
            if (!models.Remove(model.Model, out var other))
            {
                differences++;
                continue;
            }

            differences += Compare(model.Calls, other.Calls) + Compare(model.FreshInput, other.FreshInput) + Compare(model.CacheRead, other.CacheRead)
                           + Compare(model.CacheWrite5M, other.CacheWrite5M) + Compare(model.CacheWrite1H, other.CacheWrite1H) + Compare(model.Output, other.Output);
        }

        differences += models.Count;

        var gaps = baseline.GapBuckets.ToDictionary(KeyOf, gap => gap, StringComparer.Ordinal);
        foreach (var gap in report.GapBuckets)
        {
            if (!gaps.Remove(KeyOf(gap), out var other))
            {
                differences++;
                continue;
            }

            differences += Compare(gap.Calls, other.Calls) + Compare(gap.CacheWrite, other.CacheWrite);
        }

        differences += gaps.Count;

        var tools = baseline.Tools.ToDictionary(tool => tool.Tool, tool => tool, StringComparer.Ordinal);
        foreach (var tool in report.Tools)
        {
            if (!tools.Remove(tool.Tool, out var other))
            {
                differences++;
                continue;
            }

            differences += Compare(tool.Calls, other.Calls) + Compare(tool.ResultBytes, other.ResultBytes);
        }

        return differences + tools.Count;
    }

    private static int Compare(long current, long baseline)
    {
        return current == baseline ? 0 : 1;
    }

    private static void WriteTable(string[] headers, IEnumerable<string[]> rows, IEnumerable<string>? deltas, string deltaHeader)
    {
        var allHeaders = deltas is null ? headers : [..headers, deltaHeader];
        WriteLine($"| {string.Join(" | ", allHeaders)} |");
        WriteLine($"|{string.Concat(Enumerable.Repeat("---|", allHeaders.Length))}");

        using var delta = deltas?.GetEnumerator();
        foreach (var row in rows)
        {
            var cells = delta is not null && delta.MoveNext() ? [..row, delta.Current] : row;
            WriteLine($"| {string.Join(" | ", cells)} |");
        }
    }

    private static string Number(long value)
    {
        return value.ToString("N0", CultureInfo.InvariantCulture);
    }

    private static string Price(decimal value)
    {
        return $"${value.ToString("N2", CultureInfo.InvariantCulture)}";
    }

    private static void WriteLine(string line = "")
    {
        AnsiConsole.WriteLine(line);
    }

    private sealed record Transcript(string Path, string Scope, string Session, string Agent);

    private sealed record ModelPrice(decimal Input, decimal CacheWrite5M, decimal CacheWrite1H, decimal CacheRead, decimal Output);

    /// What the scanner has to remember between one counted call and the next within a single transcript.
    private sealed class CallState
    {
        public DateTimeOffset? PreviousCall { get; set; }

        /// Cache read plus cache write of the previous call, so the prefix this call should be able to read.
        /// Negative until the first call of the transcript has been counted.
        public long PreviousPrefix { get; set; } = -1;

        public long ResultBytesSincePreviousCall { get; set; }

        /// False when the previous assistant message ended the turn without asking for a tool, which is where
        /// a new user or teammate message resumes the conversation.
        public bool ToolUseSincePreviousCall { get; set; }
    }

    private sealed class FileScan(string scope, string session, string agent)
    {
        public string Scope { get; } = scope;

        public string Session { get; } = session;

        public string Agent { get; } = agent;

        public long Calls { get; set; }

        public long CacheRead { get; set; }

        public long CacheWrite { get; set; }

        public long FreshInput { get; set; }

        public long Output { get; set; }

        public long CallsAboveThreshold { get; set; }

        public long ColdStart { get; set; }

        public long Rewritten { get; set; }

        public long ChurnCalls { get; set; }

        public long ChurnResultBytes { get; set; }

        public long TurnBoundaries { get; set; }

        public string? FirstTimestamp { get; set; }

        public List<long> Contexts { get; } = [];

        public Dictionary<string, ModelRow> Models { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, GapRow> Gaps { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> ToolCalls { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, long> ToolBytes { get; } = new(StringComparer.Ordinal);

        public List<LargeResultRow> LargestResults { get; set; } = [];

        public void AddToolCall(string tool)
        {
            ToolCalls[tool] = ToolCalls.GetValueOrDefault(tool) + 1;
        }

        public void AddToolResult(string tool, long bytes)
        {
            ToolBytes[tool] = ToolBytes.GetValueOrDefault(tool) + bytes;
        }
    }

    private sealed class UsageReport
    {
        public string? Since { get; init; }

        public string? Until { get; init; }

        public string? Project { get; init; }

        public int TranscriptCount { get; init; }

        public List<FileRow> Files { get; init; } = [];

        public List<ModelRow> Models { get; set; } = [];

        public List<GapRow> GapBuckets { get; init; } = [];

        public List<ChurnRow> CacheWriteAttribution { get; set; } = [];

        public List<ToolRow> Tools { get; set; } = [];

        public List<LargeResultRow> LargestResults { get; set; } = [];
    }

    private sealed class FileRow
    {
        public string Scope { get; init; } = "";

        public string Session { get; init; } = "";

        public string Agent { get; init; } = "";

        public long Calls { get; init; }

        public long CacheRead { get; init; }

        public long CacheWrite { get; init; }

        public long FreshInput { get; init; }

        public long Output { get; init; }

        public string FirstTimestamp { get; init; } = "";

        public long MedianContext { get; init; }

        public long P90Context { get; init; }

        public double LargeContextShare { get; init; }
    }

    private sealed class ModelRow
    {
        public string Model { get; init; } = "";

        public long Calls { get; set; }

        public long FreshInput { get; set; }

        public long CacheRead { get; set; }

        public long CacheWrite5M { get; set; }

        public long CacheWrite1H { get; set; }

        public long Output { get; set; }

        public long Rewritten { get; set; }

        public decimal CostUsd { get; set; }
    }

    private sealed class ChurnRow
    {
        public string Scope { get; init; } = "";

        public string Agent { get; init; } = "";

        public long Calls { get; set; }

        public long CacheWrite { get; set; }

        public long ColdStart { get; set; }

        public long Rewritten { get; set; }

        public long ChurnCalls { get; set; }

        public long ChurnResultBytes { get; set; }

        public long TurnBoundaries { get; set; }

        public long NewMaterial => CacheWrite - ColdStart - Rewritten;
    }

    private sealed class GapRow
    {
        public string Scope { get; init; } = "";

        public string Bucket { get; init; } = "";

        public long Calls { get; set; }

        public long CacheWrite { get; set; }
    }

    private sealed class ToolRow
    {
        public string Tool { get; init; } = "";

        public long Calls { get; init; }

        public long ResultBytes { get; init; }
    }

    private sealed class LargeResultRow
    {
        public string Tool { get; init; } = "";

        public long Bytes { get; init; }

        public string Session { get; init; } = "";

        public string Timestamp { get; init; } = "";
    }
}
