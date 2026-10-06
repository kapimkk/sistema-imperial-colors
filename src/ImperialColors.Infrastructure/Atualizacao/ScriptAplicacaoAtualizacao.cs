namespace ImperialColors.Infrastructure.Atualizacao;

/// <summary>
/// Monta o script PowerShell que troca os arquivos depois que o aplicativo fecha.
///
/// A troca não pode acontecer dentro do próprio processo: o Windows mantém o executável e as
/// DLLs travados enquanto ele roda. Por isso um processo auxiliar espera este fechar, copia
/// por cima e reabre.
///
/// É o trecho mais perigoso da funcionalidade — se ele falhar no meio, a instalação fica
/// metade nova e metade velha e o sistema não abre mais, num caixa, sem ninguém por perto que
/// saiba consertar. As três garantias abaixo existem por isso:
///
/// 1. <b>Nunca copiar com o app vivo.</b> Se o processo não fechar dentro do prazo, o script
///    aborta sem ter tocado em arquivo nenhum. Prosseguir "na esperança" é o que produz a
///    instalação corrompida, porque a cópia falha exatamente no meio.
/// 2. <b>Cópia de segurança antes e restauração no erro.</b> Qualquer falha durante a troca
///    (antivírus segurando um arquivo, disco cheio) desfaz tudo e reabre a versão antiga.
///    Ficar na versão anterior é um problema; ficar sem sistema é outro, muito maior.
/// 3. <b>Registro em arquivo.</b> O auxiliar roda sem janela; sem log, uma falha vira "o
///    sistema não abriu mais" e não há por onde começar a investigar.
/// </summary>
internal static class ScriptAplicacaoAtualizacao
{
    /// <summary>
    /// Arquivos que a atualização preserva por nome, em qualquer nível da pasta: são dados do
    /// cliente, não do pacote. O <c>.env</c> guarda credenciais do banco e dados da empresa —
    /// sobrescrever apontaria o PDV para um banco que não existe.
    ///
    /// O <c>ImperialSync.env</c> (senha do usuário do ImperialSync no banco e segredos do site) mora
    /// ao lado do <c>ImperialSync.exe</c>, na mesma pasta do sistema. O pacote de release não o
    /// contém e esta troca só COPIA o que está no pacote (nada é apagado), então hoje ele já
    /// sobreviveria; a entrada na lista garante que um pacote futuro nunca o sobrescreva.
    ///
    /// O banco de contingência (vendas gravadas offline, ainda não sincronizadas) não está
    /// nesta lista porque não precisa: ele mora em %LOCALAPPDATA%\ImperialColors, fora da
    /// pasta de instalação, então a troca de arquivos nunca o alcança.
    /// </summary>
    public static readonly string[] ArquivosPreservados = [".env", "ImperialSync.env"];

    public static string Gerar()
    {
        var preservados = string.Join(", ", ArquivosPreservados.Select(n => $"'{n}'"));

        return $$"""
param(
    [Parameter(Mandatory = $true)][string]$Origem,
    [Parameter(Mandatory = $true)][string]$Destino,
    [Parameter(Mandatory = $true)][string]$Executavel,
    [Parameter(Mandatory = $true)][string]$Backup,
    [Parameter(Mandatory = $true)][string]$Log,
    [Parameter(Mandatory = $true)][int]$PidEspera
)

$ErrorActionPreference = 'Stop'
$preservados = @({{preservados}})

function Escrever($mensagem) {
    $linha = "[{0:yyyy-MM-dd HH:mm:ss}] {1}" -f (Get-Date), $mensagem
    Add-Content -LiteralPath $Log -Value $linha -Encoding UTF8
}

function Reabrir() {
    try { Start-Process -FilePath $Executavel -WorkingDirectory $Destino } catch {
        Escrever "FALHA ao reabrir: $($_.Exception.Message)"
    }
}

Escrever "Iniciando. Origem=$Origem Destino=$Destino Pid=$PidEspera"

# --- 1. Esperar o aplicativo fechar -----------------------------------------------------
# Cinco minutos cobrem com folga o fechamento normal (o app fecha logo depois de disparar
# este script). Se estourar, alguma coisa segurou a janela e copiar seria destrutivo.
$limite = (Get-Date).AddMinutes(5)
while ((Get-Date) -lt $limite) {
    if (-not (Get-Process -Id $PidEspera -ErrorAction SilentlyContinue)) { break }
    Start-Sleep -Milliseconds 500
}

if (Get-Process -Id $PidEspera -ErrorAction SilentlyContinue) {
    Escrever "ABORTADO: o aplicativo (PID $PidEspera) nao fechou. Nenhum arquivo foi alterado."
    exit 2
}

Escrever "Aplicativo fechado."
Start-Sleep -Milliseconds 800  # deixa o Windows liberar os handles do processo encerrado

# --- 2. Copia de seguranca --------------------------------------------------------------
try {
    if (Test-Path -LiteralPath $Backup) { Remove-Item -LiteralPath $Backup -Recurse -Force }
    New-Item -ItemType Directory -Path $Backup -Force | Out-Null
    # -Path, nunca -LiteralPath: com -LiteralPath o '*' e tratado como nome literal de
    # arquivo, a copia leva zero arquivo e nao levanta erro nenhum. A copia de seguranca
    # ficava vazia em silencio e a restauracao "dava certo" sem restaurar nada.
    Copy-Item -Path (Join-Path $Destino '*') -Destination $Backup -Recurse -Force

    # Conferencia explicita pelo mesmo motivo: uma rede de protecao que falha calada e pior
    # que nenhuma, porque so se descobre no dia em que ela precisava funcionar.
    $itensBackup = @(Get-ChildItem -LiteralPath $Backup -Recurse -File).Count
    if ($itensBackup -eq 0) {
        throw "A copia de seguranca ficou vazia."
    }
    Escrever "Copia de seguranca criada em $Backup ($itensBackup arquivos)"
}
catch {
    Escrever "ABORTADO: nao foi possivel criar a copia de seguranca. $($_.Exception.Message)"
    Reabrir
    exit 3
}

# --- 3. Troca dos arquivos --------------------------------------------------------------
try {
    $copiados = 0
    Get-ChildItem -LiteralPath $Origem -Recurse -File | ForEach-Object {
        if ($preservados -contains $_.Name) { return }

        $relativo = $_.FullName.Substring($Origem.Length).TrimStart('\', '/')
        $destinoArquivo = Join-Path $Destino $relativo
        $pastaDestino = Split-Path -Parent $destinoArquivo
        if (-not (Test-Path -LiteralPath $pastaDestino)) {
            New-Item -ItemType Directory -Path $pastaDestino -Force | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $destinoArquivo -Force
        $copiados++
    }
    Escrever "Atualizacao aplicada com sucesso ($copiados arquivos)."
}
catch {
    Escrever "FALHA ao copiar: $($_.Exception.Message)"
    Escrever "Restaurando a versao anterior..."
    try {
        Copy-Item -Path (Join-Path $Backup '*') -Destination $Destino -Recurse -Force
        Escrever "Versao anterior restaurada."
    }
    catch {
        Escrever "CRITICO: a restauracao tambem falhou. $($_.Exception.Message)"
        Escrever "A copia intacta continua em: $Backup"
        Reabrir
        exit 4
    }
    Reabrir
    exit 5
}

# --- 4. Limpeza e reabertura ------------------------------------------------------------
try { Remove-Item -LiteralPath $Backup -Recurse -Force } catch {
    Escrever "Aviso: nao foi possivel remover a copia de seguranca em $Backup"
}

Start-Sleep -Milliseconds 500
Reabrir
Escrever "Concluido."
exit 0
""";
    }
}
