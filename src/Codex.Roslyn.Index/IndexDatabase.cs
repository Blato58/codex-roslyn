using Codex.Roslyn.Abstractions.Dtos;
using Microsoft.Data.Sqlite;

namespace Codex.Roslyn.Index;

public sealed class IndexDatabase
{
    public void EnsureSchema(string indexPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
        using var connection = Open(indexPath);
        ExecuteNonQuery(connection, "pragma journal_mode = wal;");
        ExecuteNonQuery(connection, SchemaSql);
    }

    public void ReplaceIndex(string indexPath, RepoIdentity identity, RepoScanResult scanResult, IReadOnlyDictionary<string, IReadOnlyList<SyntaxDeclaration>> declarationsByFile)
    {
        EnsureSchema(indexPath);
        using var connection = Open(indexPath);
        using var transaction = connection.BeginTransaction();

        foreach (var table in new[] { "symbol_fts", "file_fts", "diagnostic", "inheritance_edge", "call_edge", "reference_edge", "symbol", "declaration", "document", "project_target", "project", "file", "solution", "repo" })
        {
            ExecuteNonQuery(connection, $"delete from {table};", transaction);
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        ExecuteNonQuery(
            connection,
            "insert into repo (repo_id, root_path, git_head, git_remote_hash, created_utc, updated_utc, schema_version) values ($repo_id, $root_path, null, $git_remote_hash, $now, $now, $schema_version);",
            transaction,
            ("$repo_id", identity.RepoId),
            ("$root_path", identity.RepoRoot),
            ("$git_remote_hash", identity.GitRemoteUrl ?? string.Empty),
            ("$now", now),
            ("$schema_version", IndexConstants.SchemaVersion));

        foreach (var solution in scanResult.SolutionPaths)
        {
            ExecuteNonQuery(
                connection,
                "insert into solution (solution_id, repo_id, relative_path, display_name, file_hash, last_seen_utc, is_default) values ($solution_id, $repo_id, $relative_path, $display_name, null, $now, 0);",
                transaction,
                ("$solution_id", CreateSolutionId(solution)),
                ("$repo_id", identity.RepoId),
                ("$relative_path", solution),
                ("$display_name", Path.GetFileNameWithoutExtension(solution)),
                ("$now", now));
        }

        foreach (var file in scanResult.SourceFiles)
        {
            var fileId = CreateFileId(identity.RepoId, file.RelativePath);
            ExecuteNonQuery(
                connection,
                "insert into file (file_id, repo_id, relative_path, extension, size_bytes, mtime_utc, content_hash, is_generated, last_indexed_utc) values ($file_id, $repo_id, $relative_path, $extension, $size_bytes, $mtime_utc, $content_hash, $is_generated, $now);",
                transaction,
                ("$file_id", fileId),
                ("$repo_id", identity.RepoId),
                ("$relative_path", file.RelativePath),
                ("$extension", file.Extension),
                ("$size_bytes", file.HashInfo.SizeBytes),
                ("$mtime_utc", file.HashInfo.MTimeUtc.ToString("O")),
                ("$content_hash", file.HashInfo.ContentHash),
                ("$is_generated", file.IsGenerated ? 1 : 0),
                ("$now", now));

            var outline = declarationsByFile.TryGetValue(file.RelativePath, out var declarations)
                ? string.Join('\n', declarations.Select(declaration => $"{declaration.Kind} {declaration.SignatureShort}"))
                : string.Empty;
            ExecuteNonQuery(
                connection,
                "insert into file_fts (relative_path, outline, namespaces, type_names) values ($relative_path, $outline, $namespaces, $type_names);",
                transaction,
                ("$relative_path", file.RelativePath),
                ("$outline", outline),
                ("$namespaces", declarations is null ? string.Empty : string.Join(' ', declarations.Select(declaration => declaration.Namespace).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct())),
                ("$type_names", declarations is null ? string.Empty : string.Join(' ', declarations.Where(declaration => IsTypeKind(declaration.Kind)).Select(declaration => declaration.Name).Distinct())));

            if (declarations is null)
            {
                continue;
            }

            foreach (var declaration in declarations)
            {
                ExecuteNonQuery(
                    connection,
                    """
                    insert into declaration (
                      declaration_id, file_id, document_id, solution_id, project_id, declared_symbol_id,
                      kind, name, namespace, containing_type, accessibility, modifiers, arity,
                      signature_short, signature_hash, start_line, start_col, end_line, end_col,
                      syntax_hash, semantic_state)
                    values (
                      $declaration_id, $file_id, null, null, null, $symbol_id,
                      $kind, $name, $namespace, $containing_type, $accessibility, $modifiers, $arity,
                      $signature_short, $signature_hash, $start_line, $start_col, $end_line, $end_col,
                      $syntax_hash, 'syntax_only');
                    """,
                    transaction,
                    ("$declaration_id", declaration.DeclarationId),
                    ("$file_id", fileId),
                    ("$symbol_id", declaration.SymbolId),
                    ("$kind", declaration.Kind),
                    ("$name", declaration.Name),
                    ("$namespace", declaration.Namespace),
                    ("$containing_type", declaration.ContainingType ?? string.Empty),
                    ("$accessibility", declaration.Accessibility ?? string.Empty),
                    ("$modifiers", declaration.Modifiers),
                    ("$arity", declaration.Arity),
                    ("$signature_short", declaration.SignatureShort),
                    ("$signature_hash", declaration.SignatureHash),
                    ("$start_line", declaration.StartLine),
                    ("$start_col", declaration.StartColumn),
                    ("$end_line", declaration.EndLine),
                    ("$end_col", declaration.EndColumn),
                    ("$syntax_hash", declaration.SyntaxHash));

                ExecuteNonQuery(
                    connection,
                    """
                    insert or ignore into symbol (
                      symbol_id, solution_id, project_id, target_id, kind, name, full_name, metadata_name,
                      doc_comment_id, assembly_name, namespace, containing_type, accessibility,
                      signature_short, signature_hash, source_file_id, source_start_line, source_start_col,
                      is_external, confidence)
                    values (
                      $symbol_id, null, null, null, $kind, $name, $full_name, $full_name,
                      null, null, $namespace, $containing_type, $accessibility,
                      $signature_short, $signature_hash, $file_id, $start_line, $start_col,
                      0, 'syntax_only');
                    """,
                    transaction,
                    ("$symbol_id", declaration.SymbolId),
                    ("$kind", declaration.Kind),
                    ("$name", declaration.Name),
                    ("$full_name", BuildFullName(declaration)),
                    ("$namespace", declaration.Namespace),
                    ("$containing_type", declaration.ContainingType ?? string.Empty),
                    ("$accessibility", declaration.Accessibility ?? string.Empty),
                    ("$signature_short", declaration.SignatureShort),
                    ("$signature_hash", declaration.SignatureHash),
                    ("$file_id", fileId),
                    ("$start_line", declaration.StartLine),
                    ("$start_col", declaration.StartColumn));

                ExecuteNonQuery(
                    connection,
                    "insert into symbol_fts (name, full_name, namespace, containing_type, signature_short) values ($name, $full_name, $namespace, $containing_type, $signature_short);",
                    transaction,
                    ("$name", declaration.Name),
                    ("$full_name", BuildFullName(declaration)),
                    ("$namespace", declaration.Namespace),
                    ("$containing_type", declaration.ContainingType ?? string.Empty),
                    ("$signature_short", declaration.SignatureShort));
            }
        }

        transaction.Commit();
    }

    public IndexStatusSummary GetStatus(string indexPath, RepoIdentity identity)
    {
        if (!File.Exists(indexPath))
        {
            return new IndexStatusSummary
            {
                RepoRoot = identity.RepoRoot,
                RepoId = identity.RepoId,
                CachePath = indexPath,
                IndexState = "missing",
                WorkspaceState = "cold",
                Message = "Cold index is missing. Run 'dotnet-roslyn-mcp index --repo <path>'."
            };
        }

        using var connection = Open(indexPath);
        var schemaVersion = Convert.ToInt32(ExecuteScalar(connection, "select schema_version from repo limit 1;") ?? 0);
        var solutionCount = Convert.ToInt32(ExecuteScalar(connection, "select count(*) from solution;") ?? 0);
        var fileCount = Convert.ToInt32(ExecuteScalar(connection, "select count(*) from file;") ?? 0);
        var declarationCount = Convert.ToInt32(ExecuteScalar(connection, "select count(*) from declaration;") ?? 0);
        var symbolCount = Convert.ToInt32(ExecuteScalar(connection, "select count(*) from symbol;") ?? 0);
        var stale = IsStale(connection, identity.RepoRoot);

        return new IndexStatusSummary
        {
            RepoRoot = identity.RepoRoot,
            RepoId = identity.RepoId,
            CachePath = indexPath,
            IndexState = stale ? "stale" : "fresh",
            WorkspaceState = "cold",
            SchemaVersion = schemaVersion,
            SolutionCount = solutionCount,
            FilesIndexed = fileCount,
            DeclarationsIndexed = declarationCount,
            SymbolsIndexed = symbolCount,
            Message = stale ? "Cold index exists but at least one indexed file changed." : "Cold index is fresh."
        };
    }

    public IReadOnlyList<SolutionSummary> GetSolutions(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return [];
        }

        using var connection = Open(indexPath);
        using var command = connection.CreateCommand();
        command.CommandText = "select solution_id, relative_path, display_name, is_default from solution order by relative_path;";
        using var reader = command.ExecuteReader();
        var results = new List<SolutionSummary>();

        while (reader.Read())
        {
            results.Add(new SolutionSummary
            {
                SolutionId = reader.GetString(0),
                Path = reader.GetString(1),
                DisplayName = reader.GetString(2),
                IsActive = reader.GetInt32(3) != 0,
                Reason = "Read from cold SQLite index"
            });
        }

        return results;
    }

    public IReadOnlyList<SymbolSearchResult> SearchSymbols(string indexPath, string query, string? kind, int maxItems)
    {
        if (!File.Exists(indexPath))
        {
            return [];
        }

        using var connection = Open(indexPath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            select s.symbol_id, s.kind, s.name, s.full_name, f.relative_path, s.source_start_line, s.confidence
            from symbol s
            join file f on f.file_id = s.source_file_id
            where ($kind = '' or s.kind = $kind)
              and (
                s.name like $like
                or s.full_name like $like
                or s.signature_short like $like
                or exists (
                    select 1 from symbol_fts
                    where symbol_fts match $fts
                      and symbol_fts.name = s.name
                )
              )
            order by s.full_name
            limit $limit;
            """;
        command.Parameters.AddWithValue("$kind", kind ?? string.Empty);
        command.Parameters.AddWithValue("$like", "%" + query + "%");
        command.Parameters.AddWithValue("$fts", EscapeFtsQuery(query));
        command.Parameters.AddWithValue("$limit", Math.Clamp(maxItems, 1, 500));

        using var reader = command.ExecuteReader();
        var results = new List<SymbolSearchResult>();
        while (reader.Read())
        {
            results.Add(new SymbolSearchResult
            {
                SymbolId = reader.GetString(0),
                Kind = reader.GetString(1),
                Name = reader.GetString(2),
                DisplayName = reader.GetString(3),
                File = reader.GetString(4),
                Line = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
                Confidence = reader.GetString(6)
            });
        }

        return results;
    }

    public IReadOnlyList<DocumentOutlineItem> GetDocumentOutline(string indexPath, string file, int maxItems)
    {
        if (!File.Exists(indexPath))
        {
            return [];
        }

        using var connection = Open(indexPath);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            select d.declaration_id, d.kind, d.name, d.signature_short, d.start_line, d.start_col, d.end_line, d.end_col, d.semantic_state
            from declaration d
            join file f on f.file_id = d.file_id
            where f.relative_path = $file
            order by d.start_line, d.start_col
            limit $limit;
            """;
        command.Parameters.AddWithValue("$file", file.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'));
        command.Parameters.AddWithValue("$limit", Math.Clamp(maxItems, 1, 500));

        using var reader = command.ExecuteReader();
        var results = new List<DocumentOutlineItem>();
        while (reader.Read())
        {
            results.Add(new DocumentOutlineItem
            {
                DeclarationId = reader.GetString(0),
                Kind = reader.GetString(1),
                Name = reader.GetString(2),
                DisplayName = reader.GetString(3),
                StartLine = reader.GetInt32(4),
                StartColumn = reader.GetInt32(5),
                EndLine = reader.GetInt32(6),
                EndColumn = reader.GetInt32(7),
                Confidence = reader.GetString(8)
            });
        }

        return results;
    }

    public bool ContainsFile(string indexPath, string file)
    {
        if (!File.Exists(indexPath))
        {
            return false;
        }

        using var connection = Open(indexPath);
        using var command = connection.CreateCommand();
        command.CommandText = "select 1 from file where relative_path = $file limit 1;";
        command.Parameters.AddWithValue("$file", file.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'));
        return command.ExecuteScalar() is not null;
    }

    public void RecordSemanticSymbol(string indexPath, RepoIdentity identity, SemanticSymbolResult symbol)
    {
        if (!File.Exists(indexPath))
        {
            return;
        }

        using var connection = Open(indexPath);
        ExecuteNonQuery(
            connection,
            """
            insert or replace into symbol (
              symbol_id, solution_id, project_id, target_id, kind, name, full_name, metadata_name,
              doc_comment_id, assembly_name, namespace, containing_type, accessibility,
              signature_short, signature_hash, source_file_id, source_start_line, source_start_col,
              is_external, confidence)
            values (
              $symbol_id, null, null, null, $kind, $name, $full_name, $full_name,
              $doc_comment_id, $assembly_name, null, null, $accessibility,
              $signature_short, $signature_hash, $file_id, $start_line, $start_col,
              0, 'semantic');
            """,
            parameters:
            [
                ("$symbol_id", symbol.SymbolId),
                ("$kind", symbol.Kind),
                ("$name", symbol.Name),
                ("$full_name", symbol.DisplayName),
                ("$doc_comment_id", symbol.DocumentationCommentId ?? string.Empty),
                ("$assembly_name", symbol.AssemblyName ?? string.Empty),
                ("$accessibility", symbol.DeclaredAccessibility ?? string.Empty),
                ("$signature_short", symbol.DisplayName),
                ("$signature_hash", StableHash(symbol.DisplayName)),
                ("$file_id", symbol.Definition is null ? string.Empty : CreateFileId(identity.RepoId, symbol.Definition.File)),
                ("$start_line", symbol.Definition?.StartLine ?? 0),
                ("$start_col", symbol.Definition?.StartColumn ?? 0)
            ]);
    }

    public void RecordSemanticReferences(string indexPath, RepoIdentity identity, string targetSymbolId, IReadOnlyList<SemanticReferenceResult> references)
    {
        if (!File.Exists(indexPath))
        {
            return;
        }

        using var connection = Open(indexPath);
        using var transaction = connection.BeginTransaction();
        foreach (var reference in references)
        {
            var edgeId = "ref_" + StableHash($"{targetSymbolId}:{reference.File}:{reference.StartLine}:{reference.StartColumn}:{reference.ReferenceKind}")[..20];
            ExecuteNonQuery(
                connection,
                """
                insert or replace into reference_edge (
                  edge_id, solution_id, project_id, from_file_id, from_symbol_id, to_symbol_id,
                  reference_kind, start_line, start_col, end_line, end_col, confidence, computed_utc)
                values (
                  $edge_id, '', null, $from_file_id, null, $to_symbol_id,
                  $reference_kind, $start_line, $start_col, $end_line, $end_col, 'semantic', $computed_utc);
                """,
                transaction,
                ("$edge_id", edgeId),
                ("$from_file_id", CreateFileId(identity.RepoId, reference.File)),
                ("$to_symbol_id", targetSymbolId),
                ("$reference_kind", reference.ReferenceKind),
                ("$start_line", reference.StartLine),
                ("$start_col", reference.StartColumn),
                ("$end_line", reference.StartLine),
                ("$end_col", reference.StartColumn),
                ("$computed_utc", DateTimeOffset.UtcNow.ToString("O")));
        }

        transaction.Commit();
    }

    public void RecordSemanticHierarchy(string indexPath, string sourceSymbolId, IReadOnlyList<SemanticHierarchyResult> hierarchy)
    {
        if (!File.Exists(indexPath))
        {
            return;
        }

        using var connection = Open(indexPath);
        using var transaction = connection.BeginTransaction();
        foreach (var item in hierarchy)
        {
            var edgeId = "inh_" + StableHash($"{sourceSymbolId}:{item.SymbolId}:{item.RelationKind}")[..20];
            var derived = item.RelationKind is "derived" or "implementation" ? item.SymbolId : sourceSymbolId;
            var @base = item.RelationKind is "derived" or "implementation" ? sourceSymbolId : item.SymbolId;
            ExecuteNonQuery(
                connection,
                """
                insert or replace into inheritance_edge (
                  edge_id, solution_id, derived_symbol_id, base_symbol_id, relation_kind, confidence, computed_utc)
                values ($edge_id, '', $derived_symbol_id, $base_symbol_id, $relation_kind, 'semantic', $computed_utc);
                """,
                transaction,
                ("$edge_id", edgeId),
                ("$derived_symbol_id", derived),
                ("$base_symbol_id", @base),
                ("$relation_kind", item.RelationKind),
                ("$computed_utc", DateTimeOffset.UtcNow.ToString("O")));
        }

        transaction.Commit();
    }

    public void RecordSemanticCallers(string indexPath, RepoIdentity identity, IReadOnlyList<SemanticCallerResult> callers)
    {
        if (!File.Exists(indexPath))
        {
            return;
        }

        using var connection = Open(indexPath);
        using var transaction = connection.BeginTransaction();
        foreach (var caller in callers)
        {
            var edgeId = "call_" + StableHash($"{caller.CallerSymbolId}:{caller.CalleeSymbolId}:{caller.Location?.File}:{caller.Location?.StartLine}:{caller.Location?.StartColumn}")[..20];
            ExecuteNonQuery(
                connection,
                """
                insert or replace into call_edge (
                  edge_id, solution_id, caller_symbol_id, callee_symbol_id, call_kind, file_id, start_line, start_col, confidence, computed_utc)
                values ($edge_id, '', $caller_symbol_id, $callee_symbol_id, 'call', $file_id, $start_line, $start_col, 'semantic', $computed_utc);
                """,
                transaction,
                ("$edge_id", edgeId),
                ("$caller_symbol_id", caller.CallerSymbolId),
                ("$callee_symbol_id", caller.CalleeSymbolId),
                ("$file_id", caller.Location is null ? string.Empty : CreateFileId(identity.RepoId, caller.Location.File)),
                ("$start_line", caller.Location?.StartLine ?? 0),
                ("$start_col", caller.Location?.StartColumn ?? 0),
                ("$computed_utc", DateTimeOffset.UtcNow.ToString("O")));
        }

        transaction.Commit();
    }

    public void RecordSemanticDiagnostics(string indexPath, RepoIdentity identity, IReadOnlyList<SemanticDiagnosticResult> diagnostics)
    {
        if (!File.Exists(indexPath))
        {
            return;
        }

        using var connection = Open(indexPath);
        using var transaction = connection.BeginTransaction();
        foreach (var diagnostic in diagnostics)
        {
            var diagnosticId = "diag_" + StableHash($"{diagnostic.Id}:{diagnostic.File}:{diagnostic.Line}:{diagnostic.Column}:{diagnostic.Message}")[..20];
            ExecuteNonQuery(
                connection,
                """
                insert or replace into diagnostic (
                  diagnostic_id, solution_id, project_id, file_id, roslyn_id, severity, message,
                  start_line, start_col, end_line, end_col, computed_utc)
                values (
                  $diagnostic_id, '', null, $file_id, $roslyn_id, $severity, $message,
                  $start_line, $start_col, null, null, $computed_utc);
                """,
                transaction,
                ("$diagnostic_id", diagnosticId),
                ("$file_id", diagnostic.File is null ? string.Empty : CreateFileId(identity.RepoId, diagnostic.File)),
                ("$roslyn_id", diagnostic.Id),
                ("$severity", diagnostic.Severity),
                ("$message", diagnostic.Message),
                ("$start_line", diagnostic.Line ?? 0),
                ("$start_col", diagnostic.Column ?? 0),
                ("$computed_utc", DateTimeOffset.UtcNow.ToString("O")));
        }

        transaction.Commit();
    }

    private static SqliteConnection Open(string indexPath)
    {
        var connection = new SqliteConnection($"Data Source={indexPath}");
        connection.Open();
        return connection;
    }

    private static void ExecuteNonQuery(SqliteConnection connection, string sql, SqliteTransaction? transaction = null, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        }

        command.ExecuteNonQuery();
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static bool IsStale(SqliteConnection connection, string repoRoot)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "select relative_path, size_bytes, mtime_utc from file;";
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            var path = Path.Combine(repoRoot, reader.GetString(0).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path))
            {
                return true;
            }

            var info = new FileInfo(path);
            if (info.Length != reader.GetInt64(1))
            {
                return true;
            }

            var indexedMTime = DateTimeOffset.Parse(reader.GetString(2));
            var currentMTime = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero);
            if (currentMTime != indexedMTime)
            {
                return true;
            }
        }

        return false;
    }

    private static string CreateSolutionId(string relativePath)
    {
        return "sln_" + StableHash(relativePath.ToLowerInvariant())[..12];
    }

    private static string CreateFileId(string repoId, string relativePath)
    {
        return "file_" + StableHash($"{repoId}:{relativePath.ToLowerInvariant()}")[..16];
    }

    private static string StableHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string BuildFullName(SyntaxDeclaration declaration)
    {
        return string.Join(".", new[] { declaration.Namespace, declaration.ContainingType, declaration.Name }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static bool IsTypeKind(string kind)
    {
        return kind is "class" or "struct" or "interface" or "record" or "record_struct" or "enum" or "delegate";
    }

    private static string EscapeFtsQuery(string query)
    {
        var terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (terms.Length == 0)
        {
            return "\"\"";
        }

        return string.Join(" ", terms.Select(term => "\"" + term.Replace("\"", "\"\"") + "\""));
    }

    private const string SchemaSql = """
create table if not exists repo (
  repo_id text primary key,
  root_path text not null,
  git_head text null,
  git_remote_hash text null,
  created_utc text not null,
  updated_utc text not null,
  schema_version integer not null
);

create table if not exists solution (
  solution_id text primary key,
  repo_id text not null,
  relative_path text not null,
  display_name text not null,
  file_hash text null,
  last_seen_utc text not null,
  is_default integer not null default 0
);

create table if not exists project (
  project_id text primary key,
  solution_id text not null,
  relative_path text not null,
  name text not null,
  assembly_name text null,
  language text not null,
  output_kind text null,
  last_seen_utc text not null
);

create table if not exists project_target (
  target_id text primary key,
  project_id text not null,
  target_framework text not null,
  configuration text not null default 'Debug',
  runtime_identifier text null
);

create table if not exists file (
  file_id text primary key,
  repo_id text not null,
  relative_path text not null,
  extension text not null,
  size_bytes integer not null,
  mtime_utc text not null,
  content_hash text not null,
  is_generated integer not null default 0,
  last_indexed_utc text not null
);

create table if not exists document (
  document_id text primary key,
  solution_id text not null,
  project_id text not null,
  target_id text null,
  file_id text not null,
  logical_path text null
);

create table if not exists declaration (
  declaration_id text primary key,
  file_id text not null,
  document_id text null,
  solution_id text null,
  project_id text null,
  declared_symbol_id text null,
  kind text not null,
  name text not null,
  namespace text null,
  containing_type text null,
  accessibility text null,
  modifiers text null,
  arity integer null,
  signature_short text null,
  signature_hash text null,
  start_line integer not null,
  start_col integer not null,
  end_line integer not null,
  end_col integer not null,
  syntax_hash text not null,
  semantic_state text not null default 'syntax_only'
);

create table if not exists symbol (
  symbol_id text primary key,
  solution_id text null,
  project_id text null,
  target_id text null,
  kind text not null,
  name text not null,
  full_name text not null,
  metadata_name text null,
  doc_comment_id text null,
  assembly_name text null,
  namespace text null,
  containing_type text null,
  accessibility text null,
  signature_short text null,
  signature_hash text null,
  source_file_id text null,
  source_start_line integer null,
  source_start_col integer null,
  is_external integer not null default 0,
  confidence text not null default 'semantic'
);

create table if not exists reference_edge (
  edge_id text primary key,
  solution_id text not null,
  project_id text null,
  from_file_id text not null,
  from_symbol_id text null,
  to_symbol_id text not null,
  reference_kind text not null,
  start_line integer not null,
  start_col integer not null,
  end_line integer not null,
  end_col integer not null,
  confidence text not null,
  computed_utc text not null
);

create table if not exists call_edge (
  edge_id text primary key,
  solution_id text not null,
  caller_symbol_id text not null,
  callee_symbol_id text not null,
  call_kind text not null,
  file_id text not null,
  start_line integer not null,
  start_col integer not null,
  confidence text not null,
  computed_utc text not null
);

create table if not exists inheritance_edge (
  edge_id text primary key,
  solution_id text not null,
  derived_symbol_id text not null,
  base_symbol_id text not null,
  relation_kind text not null,
  confidence text not null,
  computed_utc text not null
);

create table if not exists diagnostic (
  diagnostic_id text primary key,
  solution_id text not null,
  project_id text null,
  file_id text null,
  roslyn_id text not null,
  severity text not null,
  message text not null,
  start_line integer null,
  start_col integer null,
  end_line integer null,
  end_col integer null,
  computed_utc text not null
);

create virtual table if not exists symbol_fts using fts5(
  name,
  full_name,
  namespace,
  containing_type,
  signature_short
);

create virtual table if not exists file_fts using fts5(
  relative_path,
  outline,
  namespaces,
  type_names
);
""";
}
