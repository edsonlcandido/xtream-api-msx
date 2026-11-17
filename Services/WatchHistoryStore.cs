using System.Collections.Concurrent;
using System.Text.Json;
using xtream_api_msx.Models;
using Microsoft.Extensions.Options;

namespace xtream_api_msx.Services;

/// <summary>
/// Historico de "ultimos assistidos", por usuario.
///
/// O app nao tinha estado nenhum (so cache em memoria). O historico mora em
/// memoria e e gravado em um arquivo JSON por usuario
/// ({Path}/{usuario}.json), escrito de forma atomica (grava ".tmp" e move).
///
/// Se o disco nao for gravavel (container sem volume, permissao), o
/// historico continua funcionando so em memoria — o app nao quebra.
///
/// O arquivo e reescrito inteiro a cada gravacao: o volume de dados e
/// pequeno (dezenas de itens), entao nao ha custo em manter o arquivo
/// sempre consistente com a memoria.
/// </summary>
public sealed class WatchHistoryStore
{
    private static readonly JsonSerializerOptions FileOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly ConcurrentDictionary<string, List<WatchEntry>> _items =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Versao do historico vista pela ultima vez pelo cliente (por usuario).
    /// O MSX pergunta "o historico mudou?" ao voltar para oInicio; se mudou,
    /// ele recria a tela. O marcador impede recarregar em laco.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _versions =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _diskLock = new(1, 1);
    private readonly HistoryOptions _options;
    private readonly ILogger<WatchHistoryStore> _logger;
    private bool _diskWarningLogged;

    public WatchHistoryStore(IOptions<HistoryOptions> options, ILogger<WatchHistoryStore> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Registra um item assistido: sobe para o topo da lista, tira a
    /// repeticao do mesmo item, corta o excedente e apaga o que passou do
    /// prazo.
    /// </summary>
    public async Task RecordAsync(string username, WatchEntry entry)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(entry.Kind))
        {
            return;
        }

        entry.Key = string.IsNullOrWhiteSpace(entry.Key)
            ? WatchEntry.BuildKey(entry.Kind, entry.Id)
            : entry.Key;

        var gate = _locks.GetOrAdd(username, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync();

        try
        {
            var items = await LoadAsync(username);

            items.RemoveAll(existing =>
                string.Equals(existing.Key, entry.Key, StringComparison.OrdinalIgnoreCase));

            items.Insert(0, entry);
            Trim(items);

            _items[username] = items;

            await SaveAsync(username, items);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao registrar o historico de {User}.", username);
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Ultimos itens assistidos (do mais novo para o mais antigo). Devolve
    /// uma copia: quem chama nao mexe no estado do store.
    /// </summary>
    public async Task<IReadOnlyList<WatchEntry>> RecentAsync(string username, int take)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return [];
        }

        var items = await LoadAsync(username);

        return items
            .Take(Math.Max(0, take))
            .ToList();
    }

    /// <summary>
    /// Versao do historico do usuario: quantos itens tem e quando o mais
    /// recente foi assistido. Muda sempre que um item novo entra (ou sai).
    /// </summary>
    public async Task<string> VersionAsync(string username)
    {
        var items = await RecentAsync(username, int.MaxValue);

        var newest = items.Count > 0 ? items[0].WatchedAt : DateTimeOffset.MinValue;

        return $"{items.Count}|{newest:O}";
    }

    /// <summary>
    /// Guarda a versao do historico que o cliente acabou de ver.
    /// </summary>
    public void SetVersion(string username, string version)
    {
        if (!string.IsNullOrWhiteSpace(username))
        {
            _versions[username] = version;
        }
    }

    /// <summary>
    /// True quando o historico mudou desde a ultima tela montada. O cliente
    /// (MSX) usa isso para decidir se recria a tela doInicio.
    /// </summary>
    public bool IsStale(string username, string version)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return false;
        }

        return !_versions.TryGetValue(username, out var seen) ||
               !string.Equals(seen, version, StringComparison.Ordinal);
    }

    /// <summary>
    /// Carrega o historico do usuario (da memoria; do disco so na primeira
    /// vez) e aplica o corte de prazo/tamanho.
    /// </summary>
    private async Task<List<WatchEntry>> LoadAsync(string username)
    {
        if (_items.TryGetValue(username, out var cached))
        {
            return cached;
        }

        var items = new List<WatchEntry>();
        var path = FilePath(username);

        if (File.Exists(path))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var file = JsonSerializer.Deserialize<WatchHistoryFile>(json, FileOptions);

                foreach (var item in file?.Items ?? [])
                {
                    if (string.IsNullOrWhiteSpace(item.Id) || !WatchKind.IsValid(item.Kind))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        item.Key = WatchEntry.BuildKey(item.Kind, item.Id);
                    }

                    items.Add(item);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Historico de {User} ilegivel; comecando vazio.", username);
            }
        }

        Trim(items);
        _items[username] = items;

        return items;
    }

    /// <summary>Grava o historico no disco (atomico: ".tmp" + move).</summary>
    private async Task SaveAsync(string username, List<WatchEntry> items)
    {
        await _diskLock.WaitAsync();

        try
        {
            var path = FilePath(username);
            var directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var file = new WatchHistoryFile
            {
                User = username,
                UpdatedAt = DateTimeOffset.UtcNow,
                Items = items
            };

            var json = JsonSerializer.Serialize(file, FileOptions);
            var temp = path + ".tmp";

            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex)
        {
            if (!_diskWarningLogged)
            {
                _diskWarningLogged = true;
                _logger.LogWarning(ex,
                    "Nao deu para gravar o historico em {Path}; o app segue so em memoria.",
                    FilePath(username));
            }
        }
        finally
        {
            _diskLock.Release();
        }
    }

    /// <summary>Corta o que passou do prazo e o que excede o limite.</summary>
    private void Trim(List<WatchEntry> items)
    {
        var maxItems = Math.Max(1, _options.MaxItems);

        if (items.Count > maxItems)
        {
            items.RemoveRange(maxItems, items.Count - maxItems);
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Max(1, _options.MaxAgeDays));

        items.RemoveAll(item => item.WatchedAt < cutoff);
    }

    /// <summary>Caminho do arquivo de historico do usuario.</summary>
    private string FilePath(string username) =>
        Path.Combine(Path.GetFullPath(_options.Path), Sanitize(username) + ".json");

    /// <summary>Nome de arquivo seguro (sem caminho, sem caractere especial).</summary>
    private static string Sanitize(string username)
    {
        var chars = username
            .Where(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            .ToArray();

        return chars.Length == 0 ? "user" : new string(chars);
    }

    /// <summary>Conteudo do arquivo de historico.</summary>
    private sealed class WatchHistoryFile
    {
        public string User { get; set; } = string.Empty;

        public DateTimeOffset UpdatedAt { get; set; }

        public List<WatchEntry> Items { get; set; } = [];
    }
}
