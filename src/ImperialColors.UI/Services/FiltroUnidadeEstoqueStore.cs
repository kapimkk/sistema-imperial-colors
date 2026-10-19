using ImperialColors.Domain.Constants;
using System.IO;
using System.Text.Json;

namespace ImperialColors.UI.Services;

/// <summary>
/// Guarda quais unidades de medida o operador deixou marcadas no filtro da lista de estoque.
///
/// O filtro é persistente de propósito: quem escolhe "só BD" quer ver só BD em tudo que
/// pesquisar, e só deixa de ser assim quando clica em "Limpar filtro". Por isso ele precisa
/// sobreviver a trocar de tela (o ViewModel é recriado a cada navegação) e a fechar e abrir o
/// sistema.
/// </summary>
public interface IFiltroUnidadeEstoqueStore
{
    /// <summary>Siglas marcadas, só as que existem de fato. Vazio = sem filtro.</summary>
    IReadOnlyList<string> Carregar();

    /// <summary>Grava o filtro atual. Lista vazia apaga o filtro guardado.</summary>
    void Salvar(IReadOnlyCollection<string> unidades);
}

/// <summary>
/// Persistência em um arquivo JSON pequeno em <c>%LocalAppData%\ImperialColors</c> — por
/// usuário do Windows, junto do banco de contingência do PDV.
///
/// Guardar o filtro é conveniência, não requisito: qualquer falha de leitura ou gravação (arquivo
/// corrompido, pasta sem permissão, disco cheio) é engolida. O pior resultado é o filtro voltar
/// vazio na próxima abertura — nunca uma tela de estoque que não abre.
/// </summary>
public sealed class FiltroUnidadeEstoqueStore : IFiltroUnidadeEstoqueStore
{
    private readonly string _caminho;

    public FiltroUnidadeEstoqueStore(string? caminho = null)
    {
        _caminho = caminho ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ImperialColors",
            "filtro_estoque.json");
    }

    private sealed class Conteudo
    {
        public List<string>? Unidades { get; set; }
    }

    public IReadOnlyList<string> Carregar()
    {
        try
        {
            if (!File.Exists(_caminho))
                return [];

            var conteudo = JsonSerializer.Deserialize<Conteudo>(File.ReadAllText(_caminho));
            return Normalizar(conteudo?.Unidades);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                      or NotSupportedException or System.Security.SecurityException)
        {
            return [];
        }
    }

    public void Salvar(IReadOnlyCollection<string> unidades)
    {
        try
        {
            var siglas = Normalizar(unidades);

            if (siglas.Count == 0)
            {
                if (File.Exists(_caminho))
                    File.Delete(_caminho);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_caminho)!);
            File.WriteAllText(_caminho, JsonSerializer.Serialize(new Conteudo { Unidades = siglas.ToList() }));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException
                                      or NotSupportedException or System.Security.SecurityException)
        {
            // Conveniência: ver a documentação da classe.
        }
    }

    /// <summary>Maiúsculas, sem repetição, só unidades que existem — um arquivo editado à mão ou
    /// de uma versão antiga não pode trazer uma sigla que filtraria tudo para fora.</summary>
    private static List<string> Normalizar(IEnumerable<string>? unidades)
        => (unidades ?? [])
            .Where(UnidadesMedida.EhValida)
            .Select(u => u.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();
}
