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
    PreviewServer previewServer)
{
    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        var parser = new Parser(with =>
        {
            with.CaseSensitive = false;
            with.CaseInsensitiveEnumValues = true;
            with.HelpWriter = null;
        });

        var parserResult = parser.ParseArguments<ListOptions, VacuumOptions, AnonymiseOptions, FlattenOptions, PreviewOptions>(args);

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
        var result = await repositoryVacuumService.VacuumAsync(options, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private async Task<int> HandleAnonymiseAsync(AnonymiseOptions options, CancellationToken cancellationToken)
    {
        var result = await repositoryAnonymiseService.AnonymiseAsync(options, cancellationToken);
        Console.WriteLine(result.Message);
        return 0;
    }

    private async Task<int> HandleFlattenAsync(FlattenOptions options, CancellationToken cancellationToken)
    {
        var result = await repositoryFlattenService.FlattenAsync(options, cancellationToken);
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
