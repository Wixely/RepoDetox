using CommandLine;
using CommandLine.Text;
using Microsoft.Extensions.Logging;

namespace RepoDetox;

public sealed class CliApplication(
    ILogger<CliApplication> logger,
    RepositoryAnalyzer repositoryAnalyzer,
    RepositoryAnonymiseService repositoryAnonymiseService,
    RepositoryVacuumService repositoryVacuumService,
    RepositoryFlattenService repositoryFlattenService,
    RepositoryExpungeService repositoryExpungeService,
    PreviewServer previewServer,
    IOperationReporter reporter)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parser = new Parser(with =>
        {
            with.CaseSensitive = false;
            with.CaseInsensitiveEnumValues = true;
            with.HelpWriter = null;
        });

        var parserResult = parser.ParseArguments<ListOptions, VacuumOptions, AnonymiseOptions, FlattenOptions, ExpungeOptions, PreviewOptions>(args);

        try
        {
            if (parserResult is Parsed<object> parsed)
            {
                return parsed.Value switch
                {
                    ListOptions options => await HandleListAsync(options, cancellationToken),
                    VacuumOptions options => await HandleVacuumAsync(options, cancellationToken),
                    AnonymiseOptions options => await HandleAnonymiseAsync(options, cancellationToken),
                    FlattenOptions options => await HandleFlattenAsync(options, cancellationToken),
                    ExpungeOptions options => await HandleExpungeAsync(options, cancellationToken),
                    PreviewOptions options => await previewServer.RunAsync(options, cancellationToken),
                    _ => throw new InvalidOperationException("Unsupported command line verb.")
                };
            }

            if (parserResult is NotParsed<object> notParsed)
            {
                return HandleParseErrors(notParsed, notParsed.Errors);
            }

            throw new InvalidOperationException("Unsupported parser result state.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RepoDetox failed while handling the supplied command line.");
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private async Task<int> HandleListAsync(ListOptions options, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Scanning repository: {options.RepositoryPath}");
        Console.WriteLine("This may take a while on large repositories with long history.");
        Console.WriteLine();

        var result = await repositoryAnalyzer.AnalyzeAsync(options.RepositoryPath, cancellationToken);
        RepositoryScanConsoleWriter.Write(result);
        return 0;
    }

    private async Task<int> HandleVacuumAsync(VacuumOptions options, CancellationToken cancellationToken)
    {
        if (!options.HasExplicitRepositoryPath)
        {
            Console.Error.WriteLine("Error: vacuum requires an explicit repository path. Pass --repo <path> or a positional path, even if it is just '.'.");
            return 1;
        }

        var request = new VacuumRequest(options.RepositoryPath, options.Force);
        var result = await repositoryVacuumService.VacuumAsync(request, reporter, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private async Task<int> HandleAnonymiseAsync(AnonymiseOptions options, CancellationToken cancellationToken)
    {
        if (!options.HasExplicitRepositoryPath)
        {
            Console.Error.WriteLine("Error: anonymise requires an explicit repository path. Pass --repo <path> or a positional path, even if it is just '.'.");
            return 1;
        }

        var request = new AnonymiseRequest(
            options.RepositoryPath,
            options.Force,
            options.NameMode,
            options.EmailMode,
            options.SetName,
            options.SetEmail);
        var result = await repositoryAnonymiseService.AnonymiseAsync(request, reporter, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private async Task<int> HandleFlattenAsync(FlattenOptions options, CancellationToken cancellationToken)
    {
        if (!options.HasExplicitRepositoryPath)
        {
            Console.Error.WriteLine("Error: flatten requires an explicit repository path. Pass --repo <path> or a positional path, even if it is just '.'.");
            return 1;
        }

        var request = new FlattenRequest(options.RepositoryPath, options.Force);
        var result = await repositoryFlattenService.FlattenAsync(request, reporter, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private async Task<int> HandleExpungeAsync(ExpungeOptions options, CancellationToken cancellationToken)
    {
        if (!options.HasExplicitRepositoryPath)
        {
            Console.Error.WriteLine("Error: expunge requires an explicit repository path. Pass --repo <path> or a positional path, even if it is just '.'.");
            return 1;
        }

        var secrets = new List<string>(options.Secrets.Where(secret => !string.IsNullOrEmpty(secret)));

        if (!string.IsNullOrWhiteSpace(options.SecretsFile))
        {
            if (!File.Exists(options.SecretsFile))
            {
                Console.Error.WriteLine($"Error: secrets file '{options.SecretsFile}' was not found.");
                return 1;
            }

            secrets.AddRange((await File.ReadAllLinesAsync(options.SecretsFile, cancellationToken))
                .Where(line => !string.IsNullOrEmpty(line)));
        }

        if (secrets.Count == 0)
        {
            Console.Error.WriteLine("Error: expunge requires at least one secret. Pass --secret <value> (repeatable) or --secrets-file <path>.");
            return 1;
        }

        var request = new ExpungeRequest(
            options.RepositoryPath,
            options.Force,
            secrets,
            options.Replacement,
            IncludeMessages: !options.ContentsOnly);
        var result = await repositoryExpungeService.ExpungeAsync(request, reporter, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private static int HandleParseErrors(
        ParserResult<object> parserResult,
        IEnumerable<Error> errors)
    {
        var errorList = errors.ToArray();
        var informational = errorList.All(IsInformationalError);
        var helpText = HelpText.AutoBuild(
            parserResult,
            current =>
            {
                current.Heading = "RepoDetox";
                current.Copyright = string.Empty;
                current.AdditionalNewLineAfterOption = false;
                current.MaximumDisplayWidth = 120;
                current.AddPostOptionsLine("Run '<verb> --help' for command-specific options, including '--repo <path>'.");
                return informational
                    ? current
                    : HelpText.DefaultParsingErrorsHandler(parserResult, current);
            },
            value => value);

        Console.WriteLine(helpText);

        return informational ? 0 : 1;
    }

    private static bool IsInformationalError(Error error) =>
        error is HelpRequestedError or HelpVerbRequestedError or NoVerbSelectedError or VersionRequestedError;
}
