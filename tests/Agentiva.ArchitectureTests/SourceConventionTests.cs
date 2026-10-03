using System.Text.RegularExpressions;
using Shouldly;
using Xunit;

namespace Agentiva.ArchitectureTests;

/// <summary>
/// Conventions checked against the source files themselves.
/// </summary>
/// <remarks>
/// Some rules are about how code is <em>written</em> rather than what it
/// compiles to, and are far more reliably checked by reading the source than by
/// analysing IL. Reading the static clock is the clearest example: by the time
/// it reaches IL it is an ordinary property access indistinguishable from any
/// other, but in source it is an unmistakable, greppable pattern.
/// </remarks>
public sealed class SourceConventionTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    /// <summary>Directories holding production code subject to these conventions.</summary>
    private static readonly string[] ProductionDirectories =
    [
        Path.Combine("src", "building-blocks"),
        "services",
        Path.Combine("contracts", "events")
    ];

    /// <summary>
    /// Production code must read time through <c>IClock</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Time drives risk decisions: which UTC day a loss belongs to, whether a
    /// price is stale, whether an order has expired. Code that reads the static
    /// clock cannot be tested at those boundaries, and the resulting defects
    /// appear only at particular times of day.
    /// </para>
    /// <para>
    /// Three places are legitimately exempt and are listed explicitly rather
    /// than pattern-matched: the <c>SystemClock</c> implementation itself, the
    /// integration-event base (a wire contract constructed at the edge, before
    /// any scope exists), and infrastructure adapters stamping a transport
    /// timestamp.
    /// </para>
    /// </remarks>
    [Fact]
    public void Production_code_reads_time_through_IClock()
    {
        // Matches DateTime.UtcNow, DateTimeOffset.UtcNow, DateTime.Now and any
        // namespace-qualified form of them.
        var staticClock = new Regex(
            @"\b(System\.)?DateTime(Offset)?\s*\.\s*(UtcNow|Now)\b",
            RegexOptions.Compiled);

        var exemptFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // The IClock implementation; it must read the real clock.
            "IClock.cs",

            // Published wire contracts, constructed at the service edge before a
            // DI scope exists. Their timestamp is the moment of serialisation.
            "IntegrationEvent.cs",

            // Transport adapters stamping an AMQP timestamp on a message, and
            // the Redis provider computing an age against the wall clock.
            "RabbitMqEventPublisher.cs",
            "Phase1Providers.cs"
        };

        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources())
        {
            var fileName = Path.GetFileName(file);

            if (exemptFiles.Contains(fileName))
            {
                continue;
            }

            var text = File.ReadAllText(file);

            foreach (var match in staticClock.Matches(text).Cast<Match>())
            {
                var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                offenders.Add($"{Path.GetRelativePath(RepositoryRoot, file)}:{line} — {match.Value}");
            }
        }

        offenders.ShouldBeEmpty(
            "These lines read the system clock directly. Inject IClock so that time-dependent "
            + "risk logic (daily loss windows, market data staleness, order expiry) stays testable. "
            + "If a use is genuinely at the system edge, add the file to exemptFiles with a "
            + "justification:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// No source file may contain a committed credential.
    /// </summary>
    /// <remarks>
    /// Catches the specific accident of a developer pasting a working
    /// connection string or exchange key into configuration "just to test it"
    /// and committing it. Exchange API keys are the highest-value secret in this
    /// platform, and a repository is the first place an attacker looks.
    /// </remarks>
    [Fact]
    public void No_source_file_contains_a_non_empty_secret()
    {
        // Matches a populated password/secret/key assignment in C# or JSON,
        // while allowing the deliberately blank values the repository ships.
        var patterns = new[]
        {
            new Regex(@"""(Password|password)""\s*:\s*""(?!\s*"")[^""]{3,}""", RegexOptions.Compiled),
            new Regex(@"""(SigningKey|ApiSecret|ApiKey)""\s*:\s*""(?!\s*"")(?!REPLACE_WITH)[^""]{8,}""",
                RegexOptions.Compiled),
            new Regex(@"Password\s*=\s*[^;""\s][^;""]{2,}", RegexOptions.Compiled)
        };

        var offenders = new List<string>();

        var files = EnumerateProductionSources()
            .Concat(EnumerateFiles("appsettings*.json"))
            .Distinct(StringComparer.Ordinal);

        foreach (var file in files)
        {
            var text = File.ReadAllText(file);

            foreach (var pattern in patterns)
            {
                foreach (var match in pattern.Matches(text).Cast<Match>())
                {
                    var line = text.Take(match.Index).Count(c => c == '\n') + 1;
                    offenders.Add($"{Path.GetRelativePath(RepositoryRoot, file)}:{line} — {match.Value}");
                }
            }
        }

        offenders.ShouldBeEmpty(
            "These lines look like committed credentials. Secrets belong in the environment or a "
            + "secret manager, and the repository's own configuration files must ship blank "
            + "values:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// Only the Execution Service may hold exchange credentials.
    /// </summary>
    /// <remarks>
    /// The security boundary the whole architecture rests on. If any other
    /// service — and above all the AI agent platform — could read an exchange
    /// key, the claim that the AI cannot move funds would be false regardless of
    /// which tools it is given.
    /// </remarks>
    [Fact]
    public void Only_the_execution_service_references_exchange_credentials()
    {
        var credentialReference = new Regex(
            @"\b(BINANCE__APIKEY|BINANCE__APISECRET|BinanceApiSecret|ExchangeApiSecret)\b",
            RegexOptions.Compiled);

        var offenders = new List<string>();

        foreach (var file in EnumerateProductionSources())
        {
            var relative = Path.GetRelativePath(RepositoryRoot, file);

            // The execution service is the one place these may appear.
            if (relative.Contains("execution-service", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (credentialReference.IsMatch(File.ReadAllText(file)))
            {
                offenders.Add(relative);
            }
        }

        offenders.ShouldBeEmpty(
            "Only the Execution Service may reference exchange credentials. These files break the "
            + "security boundary that keeps order execution outside the AI trust boundary:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> EnumerateProductionSources() => EnumerateFiles("*.cs");

    private static IEnumerable<string> EnumerateFiles(string pattern)
    {
        foreach (var directory in ProductionDirectories)
        {
            var full = Path.Combine(RepositoryRoot, directory);

            if (!Directory.Exists(full))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
            {
                // Build output and generated migrations are not hand-written code.
                if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    || file.Contains("Migrations", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    /// <summary>Walks up from the test assembly to the directory holding the solution file.</summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.GetFiles("*.sln").Length > 0 || directory.GetFiles("global.json").Length > 0)
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            "Could not locate the repository root from " + AppContext.BaseDirectory);
    }
}
