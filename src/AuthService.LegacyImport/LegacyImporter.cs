using System.Collections.ObjectModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace AuthService.LegacyImport;

/// <summary>Прогон: читает боевую таблицу, сопоставляет, переносит пачками и пишет отчёт в лог.</summary>
public sealed class LegacyImporter(
    LegacyUserReader reader,
    KeycloakImportClient keycloak,
    LegacyImportOptions options,
    ILogger<LegacyImporter> logger)
{
    public async Task<ImportReport> RunAsync(bool dryRun, CancellationToken cancellationToken)
    {
        ImportReport report = new();
        Stopwatch elapsed = Stopwatch.StartNew();
        List<UserRepresentation> batch = [];

        await foreach (LegacyUser user in reader.ReadAsync(cancellationToken))
        {
            MappingResult mapping = KeycloakUserMapper.Map(user, options.Keycloak);
            report.Read++;
            foreach (string note in mapping.Notes)
            {
                report.AddNote(note);
            }

            if (mapping.User is null)
            {
                report.Rejected.Add($"{user.Id} {user.Phone}: {mapping.RejectReason}");
                continue;
            }

            batch.Add(mapping.User);
            if (batch.Count >= options.BatchSize)
            {
                await FlushAsync(batch, dryRun, report, cancellationToken);
            }
        }

        await FlushAsync(batch, dryRun, report, cancellationToken);
        report.Elapsed = elapsed.Elapsed;
        Log(report, dryRun);
        return report;
    }

    private async Task FlushAsync(List<UserRepresentation> batch, bool dryRun, ImportReport report, CancellationToken cancellationToken)
    {
        if (batch.Count == 0)
        {
            return;
        }

        if (dryRun)
        {
            report.WouldImport += batch.Count;
        }
        else
        {
            foreach (ImportOutcome outcome in await keycloak.ImportAsync(batch, cancellationToken))
            {
                report.Add(outcome);
            }

            logger.LogInformation("Перенесено пачек: {Batches}, пользователей обработано: {Processed}", ++report.Batches, report.Processed);
        }

        batch.Clear();
    }

    private void Log(ImportReport report, bool dryRun)
    {
        if (dryRun)
        {
            logger.LogInformation(
                "Пробный прогон (без записи): прочитано {Read}, к переносу {WouldImport}, не переносятся {Rejected} — за {Elapsed}",
                report.Read, report.WouldImport, report.Rejected.Count, report.Elapsed);
        }
        else
        {
            logger.LogInformation(
                "Перенос: прочитано {Read}, перенесено {Added}, уже были {AlreadyImported}, конфликтов {Conflicts}, ошибок {Failed}, не переносятся {Rejected} — за {Elapsed}",
                report.Read, report.Added, report.AlreadyImported, report.Conflicts.Count, report.Failed.Count, report.Rejected.Count, report.Elapsed);
        }

        foreach ((string note, int count) in report.Notes)
        {
            logger.LogInformation("  {Note}: {Count}", note, count);
        }

        foreach (string line in report.Conflicts)
        {
            logger.LogWarning("  Конфликт: {Line}", line);
        }

        foreach (string line in report.Rejected)
        {
            logger.LogWarning("  Не переносится: {Line}", line);
        }

        foreach (string line in report.Failed)
        {
            logger.LogError("  Ошибка: {Line}", line);
        }
    }
}

public sealed class ImportReport
{
    private readonly SortedDictionary<string, int> _notes = new(StringComparer.Ordinal);

    public int Read { get; set; }
    public int Batches { get; set; }
    public int WouldImport { get; set; }
    public int Added { get; private set; }
    public int AlreadyImported { get; private set; }
    public Collection<string> Conflicts { get; } = [];
    public Collection<string> Failed { get; } = [];
    public Collection<string> Rejected { get; } = [];
    public TimeSpan Elapsed { get; set; }
    public IReadOnlyDictionary<string, int> Notes => _notes;
    public int Processed => Added + AlreadyImported + Conflicts.Count + Failed.Count;

    public void AddNote(string note) => _notes[note] = _notes.GetValueOrDefault(note) + 1;

    public void Add(ImportOutcome outcome)
    {
        string who = $"{outcome.User.Id} {outcome.User.Username}";
        switch (outcome.Status)
        {
            case ImportStatus.Added:
                Added++;
                break;
            case ImportStatus.AlreadyImported:
                AlreadyImported++;
                break;
            case ImportStatus.Conflict:
                Conflicts.Add($"{who}: {outcome.Detail}");
                break;
            default:
                Failed.Add($"{who}: {outcome.Detail}");
                break;
        }
    }
}
