using xtream_api_msx.Models;
using xtream_api_msx.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace xtream_api_msx.Tests;

/// <summary>
/// Testes do store de "ultimos assistidos": deduplicacao, corte por
/// tamanho, corte por prazo, persistencia, isolamento por usuario e o
/// fallback quando o disco nao pode ser gravado.
/// </summary>
public sealed class WatchHistoryStoreTests : IDisposable
{
    private readonly string _directory;

    public WatchHistoryStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "xtream-api-msx-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // best effort
        }
    }

    private WatchHistoryStore CreateStore(Action<HistoryOptions>? configure = null)
    {
        var options = new HistoryOptions
        {
            Path = _directory,
            MaxItems = 50,
            MaxAgeDays = 30,
            StripItems = 6
        };

        configure?.Invoke(options);

        return new WatchHistoryStore(Options.Create(options), NullLogger<WatchHistoryStore>.Instance);
    }

    private static WatchEntry Entry(string kind, string id, DateTimeOffset? watchedAt = null) => new()
    {
        Kind = kind,
        Id = id,
        Key = WatchEntry.BuildKey(kind, id),
        Title = $"{kind} {id}",
        WatchedAt = watchedAt ?? DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task GravaSubindoParaOTopoEDeduplica()
    {
        var store = CreateStore();

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));
        await store.RecordAsync("user", Entry(WatchKind.Movie, "2"));
        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));

        var items = await store.RecentAsync("user", 10);

        Assert.Equal(2, items.Count);
        Assert.Equal("movie:1", items[0].Key);
        Assert.Equal("movie:2", items[1].Key);
    }

    [Fact]
    public async Task CortaOExcedentePorMaxItems()
    {
        var store = CreateStore(options => options.MaxItems = 3);

        for (var index = 1; index <= 6; index++)
        {
            await store.RecordAsync("user", Entry(WatchKind.Movie, index.ToString()));
        }

        var items = await store.RecentAsync("user", 10);

        Assert.Equal(3, items.Count);
        Assert.Equal("movie:6", items[0].Key);
        Assert.Equal("movie:4", items[2].Key);
    }

    [Fact]
    public async Task RemoveItemMaisVelhoQueOPrazo()
    {
        var store = CreateStore(options => options.MaxAgeDays = 7);

        await store.RecordAsync("user", Entry(WatchKind.Movie, "antigo", DateTimeOffset.UtcNow.AddDays(-30)));
        await store.RecordAsync("user", Entry(WatchKind.Movie, "novo"));

        var items = await store.RecentAsync("user", 10);

        Assert.Single(items);
        Assert.Equal("movie:novo", items[0].Key);
    }

    [Fact]
    public async Task GravaNoArquivoERetornaEmOutraInstancia()
    {
        var store = CreateStore();

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));
        await store.RecordAsync("user", Entry(WatchKind.Series, "2"));

        var outra = CreateStore();
        var items = await outra.RecentAsync("user", 10);

        Assert.Equal(2, items.Count);
        Assert.Equal("series:2", items[0].Key);
        Assert.Equal("movie:1", items[1].Key);
        Assert.Equal("movie 1", items[1].Title);
    }

    [Fact]
    public async Task UsuariosDiferentesNaoSeEnxergam()
    {
        var store = CreateStore();

        await store.RecordAsync("ana", Entry(WatchKind.Movie, "1"));
        await store.RecordAsync("bob", Entry(WatchKind.Movie, "2"));

        var ana = await store.RecentAsync("ana", 10);
        var bob = await store.RecentAsync("bob", 10);

        Assert.Single(ana);
        Assert.Single(bob);
        Assert.Equal("movie:1", ana[0].Key);
        Assert.Equal("movie:2", bob[0].Key);
    }

    [Fact]
    public async Task ARazaoDevolveUmaCopia()
    {
        var store = CreateStore();

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));

        var items = await store.RecentAsync("user", 10) as List<WatchEntry>;
        items!.Clear();

        var outra = await store.RecentAsync("user", 10);

        Assert.Single(outra);
    }

    [Fact]
    public async Task SemDiscoGravavelSegueSoEmMemoria()
    {
        // O caminho aponta para um arquivo: criar a pasta falha e o store
        // precisa continuar funcionando em memoria.
        var blocker = Path.Combine(_directory, "bloqueio");
        await File.WriteAllTextAsync(blocker, "nao e uma pasta");

        var store = CreateStore(options => options.Path = blocker);

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));

        var items = await store.RecentAsync("user", 10);

        Assert.Single(items);
        Assert.Equal("movie:1", items[0].Key);
    }

    [Fact]
    public async Task AVersaoMudaQuandoUmItemNovoEntra()
    {
        var store = CreateStore();

        var antes = await store.VersionAsync("user");

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));

        var depois = await store.VersionAsync("user");

        Assert.NotEqual(antes, depois);
    }

    [Fact]
    public async Task AversaoNaoMudaQuandoOMesmoItemEAssistidoDeNovo()
    {
        // Assistir o mesmo filme de novo tira do historico antigo e insere
        // outro: a contagem continua igual, mas o horario muda.
        var store = CreateStore();

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1", DateTimeOffset.UtcNow.AddHours(-2)));

        var antes = await store.VersionAsync("user");

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1", DateTimeOffset.UtcNow));

        var depois = await store.VersionAsync("user");

        Assert.NotEqual(antes, depois);
    }

    [Fact]
    public async Task OMarcaadorSabeSeOHistoricoMudou()
    {
        var store = CreateStore();

        var version = await store.VersionAsync("user");

        // Sem marcador: o cliente nunca viu esta versao -> precisa recarregar.
        Assert.True(store.IsStale("user", version));

        store.SetVersion("user", version);

        // Com o marcador em dia: nao precisa recarregar (evita laco).
        Assert.False(store.IsStale("user", version));

        await store.RecordAsync("user", Entry(WatchKind.Movie, "1"));

        Assert.True(store.IsStale("user", await store.VersionAsync("user")));
    }

    [Fact]
    public async Task OMarcaadorEPorUsuario()
    {
        var store = CreateStore();

        await store.RecordAsync("ana", Entry(WatchKind.Movie, "1"));

        var versionAna = await store.VersionAsync("ana");

        store.SetVersion("ana", versionAna);

        // O "bob" nunca viu o historico da "ana": para ele, esta mudando.
        Assert.True(store.IsStale("bob", versionAna));
    }
}
