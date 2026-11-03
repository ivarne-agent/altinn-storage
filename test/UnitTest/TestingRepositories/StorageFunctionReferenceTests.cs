using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Altinn.Platform.Storage.UnitTest.Utils;
using Npgsql;
using Xunit;

namespace Altinn.Platform.Storage.UnitTest.TestingRepositories;

/// <summary>
/// Guards against a migration dropping a function or procedure that another function or the code
/// still calls. Every <c>storage.name(</c> call in the function sources and the Storage C# sources
/// must name a function, procedure, table, view or type that exists after all migrations.
/// </summary>
[Collection("StoragePostgreSQL")]
public partial class StorageFunctionReferenceTests
{
    [Fact]
    public async Task EveryCalledStorageFunction_ExistsAfterMigrations()
    {
        string root = FindRepositoryRoot();
        string storage = Path.Combine(root, "src", "Storage");
        Dictionary<string, SortedSet<string>> references = new(StringComparer.Ordinal);
        foreach (
            string file in Directory.EnumerateFiles(
                Path.Combine(storage, "Migration", "FunctionsAndProcedures"),
                "*.sql"
            )
        )
        {
            AddReferences(references, root, file, StripSqlComments(File.ReadAllText(file)));
        }

        foreach (
            string file in Directory
                .EnumerateFiles(storage, "*.cs", SearchOption.AllDirectories)
                .Where(file => !IsBuildOutput(storage, file))
        )
        {
            AddReferences(references, root, file, StripCSharpCommentLines(File.ReadAllText(file)));
        }

        HashSet<string> existing = await ReadStorageObjectNames();

        List<string> missing =
        [
            .. references
                .Where(reference => !existing.Contains(reference.Key))
                .OrderBy(reference => reference.Key, StringComparer.Ordinal)
                .Select(reference =>
                    $"storage.{reference.Key} (called in {string.Join(", ", reference.Value)})"
                ),
        ];
        Assert.NotEmpty(references);
        Assert.True(
            missing.Count == 0,
            $"Called but missing after migrations:{Environment.NewLine}{string.Join(Environment.NewLine, missing)}"
        );
    }

    private static void AddReferences(
        Dictionary<string, SortedSet<string>> references,
        string root,
        string file,
        string text
    )
    {
        foreach (string line in text.Split('\n'))
        {
            if (DefinitionOrDrop().IsMatch(line))
            {
                continue;
            }

            foreach (Match match in Call().Matches(line))
            {
                // Unquoted identifiers are case-insensitive in PostgreSQL and stored in lower case.
                string name = match.Groups["name"].Value.ToLowerInvariant();
                if (!references.TryGetValue(name, out SortedSet<string>? files))
                {
                    files = new SortedSet<string>(StringComparer.Ordinal);
                    references[name] = files;
                }

                files.Add(Path.GetRelativePath(root, file).Replace('\\', '/'));
            }
        }
    }

    /// <summary>
    /// Functions and procedures, plus the tables, views and types whose names also appear before a
    /// parenthesis (<c>INSERT INTO storage.instances (...)</c>, casts).
    /// </summary>
    private static async Task<HashSet<string>> ReadStorageObjectNames()
    {
        const string sql = """
            select p.proname from pg_proc p join pg_namespace n on n.oid = p.pronamespace where n.nspname = 'storage'
            union select c.relname from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = 'storage'
            union select t.typname from pg_type t join pg_namespace n on n.oid = t.typnamespace where n.nspname = 'storage'
            """;
        HashSet<string> names = new(StringComparer.Ordinal);
        await using NpgsqlCommand command = ServiceUtil.GetSharedDataSource().CreateCommand(sql);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static string StripSqlComments(string sql) =>
        LineComment().Replace(BlockComment().Replace(sql, string.Empty), string.Empty);

    private static string StripCSharpCommentLines(string code) =>
        string.Join(
            '\n',
            code.Split('\n')
                .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal))
        );

    private static bool IsBuildOutput(string project, string file)
    {
        string first = Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar)[0];
        return first is "bin" or "obj";
    }

    private static string FindRepositoryRoot()
    {
        for (
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            if (File.Exists(Path.Combine(directory.FullName, "Altinn.Platform.Storage.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException(
            $"No Altinn.Platform.Storage.slnx above {AppContext.BaseDirectory}."
        );
    }

    [GeneratedRegex(@"(?<![\w.])storage\.(?<name>\w+)\s*\(")]
    private static partial Regex Call();

    [GeneratedRegex(
        @"\b(CREATE\s+(OR\s+REPLACE\s+)?(FUNCTION|PROCEDURE)|DROP\s+(FUNCTION|PROCEDURE))\b",
        RegexOptions.IgnoreCase
    )]
    private static partial Regex DefinitionOrDrop();

    [GeneratedRegex(@"--[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();
}
