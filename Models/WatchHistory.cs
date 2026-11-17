namespace xtream_api_msx.Models;

/// <summary>
/// Tipos de conteudo que o app guarda no historico de "ultimos assistidos".
/// </summary>
public static class WatchKind
{
    /// <summary>Filme (VOD).</summary>
    public const string Movie = "movie";

    /// <summary>Serie (a unidade e a serie, nao o episodio).</summary>
    public const string Series = "series";

    /// <summary>Canal de TV ao vivo.</summary>
    public const string Live = "live";

    /// <summary>True quando o tipo e conhecido.</summary>
    public static bool IsValid(string? kind) =>
        kind is Movie or Series or Live;
}

/// <summary>
/// Um item do historico de "ultimos assistidos": o que o usuario assistiu,
/// quando, e os metadados (titulo, capa, ano, nota) para a tela mostrar
/// mesmo com o provedor fora do ar.
/// </summary>
public sealed class WatchEntry
{
    /// <summary>Chave unica do item ("movie:415190").</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>"movie" | "series" | "live".</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>stream_id (filme/canal) ou series_id (serie).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Categoria de origem (a serie usa isso para a pagina de detalhe).</summary>
    public string? CategoryId { get; set; }

    public string? Title { get; set; }

    /// <summary>Cartaz (filme/serie) ou logomarca (canal).</summary>
    public string? Cover { get; set; }

    public string? Year { get; set; }

    public string? Rating { get; set; }

    /// <summary>Extensao do arquivo (filmes: mp4, mkv...).</summary>
    public string? Extension { get; set; }

    /// <summary>Quando o item foi assistido (UTC).</summary>
    public DateTimeOffset WatchedAt { get; set; }

    /// <summary>Chave no formato "tipo:id".</summary>
    public static string BuildKey(string kind, string id) => $"{kind}:{id}";
}

/// <summary>
/// Opcoes do historico (secao "History" do appsettings.json).
/// </summary>
public sealed class HistoryOptions
{
    /// <summary>
    /// Pasta onde ficam os arquivos de historico (um por usuario).
    /// Sobreponivel pela variavel de ambiente "HISTORY_PATH".
    /// </summary>
    public string Path { get; set; } = "data/history";

    /// <summary>Quantos itens sao guardados por usuario.</summary>
    public int MaxItems { get; set; } = 50;

    /// <summary>Depois de quantos dias o item sai do historico.</summary>
    public int MaxAgeDays { get; set; } = 30;

    /// <summary>Quantos itens aparecem na secao "Ultimos assistidos" doInicio.</summary>
    public int StripItems { get; set; } = 6;
}
