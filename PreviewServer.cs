using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class PreviewServer(
    IConfiguration configuration,
    RepositoryAnalyzer repositoryAnalyzer,
    ILogger<PreviewServer> logger)
{
    public async Task<int> RunAsync(PreviewOptions options, CancellationToken cancellationToken = default)
    {
        var previewEnabled = configuration.GetValue<bool?>("Preview:Enabled");
        if (previewEnabled is not true)
        {
            const string message = "Preview hosting is disabled. Set Preview:Enabled to true in appsettings.json to enable it.";
            logger.LogWarning(message);
            Console.Error.WriteLine(message);
            return 1;
        }

        if (options.Port is < 1 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(options.Port), "Preview port must be between 1 and 65535.");
        }

        var prefix = $"http://127.0.0.1:{options.Port}/";

        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();

        logger.LogInformation("Now listening on: {PreviewUrl}", prefix);
        Console.WriteLine($"Now listening on: {prefix}");
        Console.WriteLine("Press Ctrl+C to stop the preview server.");

        var stopSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ConsoleCancelEventHandler? cancelHandler = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            stopSignal.TrySetResult(true);
        };

        Console.CancelKeyPress += cancelHandler;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var contextTask = listener.GetContextAsync();
                var completedTask = await Task.WhenAny(contextTask, stopSignal.Task);

                if (completedTask == stopSignal.Task)
                {
                    break;
                }

                var context = await contextTask;
                _ = HandleRequestAsync(context, options.RepositoryPath, cancellationToken);
            }
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
            listener.Stop();
        }

        return 0;
    }

    private async Task HandleRequestAsync(
        HttpListenerContext context,
        string repositoryPath,
        CancellationToken cancellationToken)
    {
        try
        {
            if (string.Equals(context.Request.RawUrl, "/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                context.Response.Close();
                return;
            }

            var analysis = await repositoryAnalyzer.AnalyzeAsync(repositoryPath, cancellationToken);
            var html = BuildHtml(analysis);
            var bytes = Encoding.UTF8.GetBytes(html);

            context.Response.StatusCode = (int)HttpStatusCode.OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to serve the preview page.");

            var html = $$"""
                        <html>
                        <head><title>RepoDetox Preview Error</title></head>
                        <body>
                            <h1>RepoDetox Preview Error</h1>
                            <pre>{{WebUtility.HtmlEncode(ex.ToString())}}</pre>
                        </body>
                        </html>
                        """;
            var bytes = Encoding.UTF8.GetBytes(html);

            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
            context.Response.Close();
        }
    }

    private static string BuildHtml(RepositoryScanResult analysis)
    {
        var items = analysis.HistoricalOnlyPaths.Count == 0
            ? "<li>No historical-only paths were found.</li>"
            : string.Join(
                Environment.NewLine,
                analysis.HistoricalOnlyPaths.Select(path => $"<li><code>{WebUtility.HtmlEncode(path)}</code></li>"));

        return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                    <meta charset="utf-8" />
                    <meta name="viewport" content="width=device-width, initial-scale=1" />
                    <title>RepoDetox Preview</title>
                    <style>
                        :root {
                            color-scheme: light;
                            font-family: Consolas, "Courier New", monospace;
                            background: #f3efe7;
                            color: #202020;
                        }
                        body {
                            margin: 0;
                            background:
                                radial-gradient(circle at top right, #f4c793 0, transparent 32%),
                                linear-gradient(180deg, #fbf7ef 0%, #efe7dc 100%);
                        }
                        main {
                            max-width: 960px;
                            margin: 0 auto;
                            padding: 3rem 1.5rem 4rem;
                        }
                        .card {
                            background: rgba(255, 255, 255, 0.86);
                            border: 1px solid rgba(32, 32, 32, 0.08);
                            border-radius: 18px;
                            box-shadow: 0 20px 50px rgba(74, 43, 9, 0.12);
                            padding: 1.5rem;
                            backdrop-filter: blur(10px);
                        }
                        h1 {
                            font-size: clamp(2rem, 4vw, 3.5rem);
                            margin-bottom: 0.4rem;
                        }
                        p {
                            line-height: 1.6;
                        }
                        dl {
                            display: grid;
                            grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
                            gap: 1rem;
                            margin: 1.5rem 0 0;
                        }
                        dt {
                            font-size: 0.8rem;
                            letter-spacing: 0.08em;
                            text-transform: uppercase;
                            color: #6c5a42;
                        }
                        dd {
                            margin: 0.35rem 0 0;
                            font-size: 1.3rem;
                            font-weight: 700;
                        }
                        ul {
                            margin: 1.5rem 0 0;
                            padding-left: 1.3rem;
                            max-height: 60vh;
                            overflow: auto;
                        }
                        code {
                            font-size: 0.95rem;
                        }
                    </style>
                </head>
                <body>
                    <main>
                        <div class="card">
                            <h1>RepoDetox</h1>
                            <p>Previewing files that still exist in history but no longer exist on the current branch.</p>
                            <dl>
                                <div>
                                    <dt>Repository</dt>
                                    <dd>{{WebUtility.HtmlEncode(analysis.RepositoryRoot)}}</dd>
                                </div>
                                <div>
                                    <dt>Branch</dt>
                                    <dd>{{WebUtility.HtmlEncode(analysis.CurrentBranch)}}</dd>
                                </div>
                                <div>
                                    <dt>Current Files</dt>
                                    <dd>{{analysis.CurrentTrackedFileCount}}</dd>
                                </div>
                                <div>
                                    <dt>Historical Paths</dt>
                                    <dd>{{analysis.HistoricalPathCount}}</dd>
                                </div>
                                <div>
                                    <dt>To Remove</dt>
                                    <dd>{{analysis.HistoricalOnlyPaths.Count}}</dd>
                                </div>
                            </dl>
                            <ul>
                                {{items}}
                            </ul>
                        </div>
                    </main>
                </body>
                </html>
                """;
    }
}
