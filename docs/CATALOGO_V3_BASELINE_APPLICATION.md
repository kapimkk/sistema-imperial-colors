# Catálogo v3 — prova das quatro falhas Application anteriores

Data da execução: 08/10/2026.

## Resultado

As quatro falhas já existem no **HEAD anterior às alterações v3**, commit
`7640301d9c2b92f984681402a55c7d5382d9b64f` (**classificação A**).
A execução das mesmas quatro asserções na cópia atual reproduziu as quatro falhas.
Nenhuma delas foi atribuída ao catálogo v3; nenhum teste foi enfraquecido para obter aprovação.

| Caso | HEAD anterior | Cópia atual v3 | Classificação |
| --- | --- | --- | --- |
| Backup em dois formatos | `.dump` 108.966 bytes; `.sql` 79.214 bytes; falha na comparação de tamanho | `.dump` 109.594 bytes; `.sql` 79.875 bytes; mesma comparação falha | A — preexistente |
| Busca com `%_` | Retorna 1 produto cujo nome não contém esses caracteres | Retorna 1 produto indevido | A — preexistente |
| Editar orçamento aprovado | Alteração de desconto 0 → 90 é aceita | Mesma alteração é aceita | A — preexistente |
| Numeração concorrente de venda externa | 2 de 5 operações falham com `DbUpdateException` | 2 de 5 operações falham com `DbUpdateException` | A — preexistente |

Contadores TRX de cada execução: **4 executados, 4 falhas, 0 ignorados**.
São execuções focadas nos quatro casos, não uma declaração de aprovação da suíte Application completa.

## Comparação controlada e isolamento

1. O código anterior foi extraído com `git archive HEAD src tests icons ImperialColors.slnx` para diretório temporário.
2. Uma segunda cópia capturou o código atual antes das mudanças adicionais de backup/multi-PC desta etapa, às **11:02 UTC**.
3. Não houve checkout, stash, reset, branch, commit ou push. O working tree de implementação permaneceu no lugar.
4. Nenhum `.env`, histórico Git, README com valores antigos, credencial ou dado de cliente foi copiado.
5. A única instrumentação no código anterior foi substituir `IntegrationTestGuard.cs` pelo guard seguro atual: opt-in explícito, host loopback e banco com sufixo `_test`. Isso é necessário porque o guard anterior carregava `.env` e, sem ele, não executaria os casos.
6. As quatro asserções originais foram preservadas. No backup, a configuração anterior via `DB_*` recebeu os mesmos dados fictícios da conexão explícita atual; as asserções desde `var data = new DateTime(...)` são idênticas.
7. Os bancos exclusivos `catalog_v3_proof_baseline_test` e `catalog_v3_proof_current_test` foram criados vazios no **mesmo PostgreSQL 18 descartável, 127.0.0.1:5438, UTC**. Nenhum banco configurado no `.env` foi aberto.
8. Um helper descartável aplicou as migrations usando `AppDbContext` com conexão explícita, sem chamar o factory anterior que procura `.env`.
9. A baseline aplicou até `20260928202838_AddComissaoItemVendaExterna`; a cópia atual aplicou também `20261007203618_AddProdutoFreteImagem`. Essa diferença aditiva de schema é a esperada para a comparação.
10. Foram utilizados o mesmo .NET 10, xUnit, máquina e `pg_dump`/`pg_restore` PostgreSQL 18. O teste de backup passou pelas verificações de existência, assinatura `PGDMP`, SQL de tabelas e ausência de `OWNER TO`, falhando somente no tamanho relativo.

A cópia atual desta prova é deliberadamente congelada. Alterações posteriores destinadas a backup/imagens não substituem retroativamente o resultado observado aqui.

## Regra/causa observada

- **Backup:** a asserção antiga pressupõe que um dump custom comprimido seja sempre menor que SQL. Em um banco quase vazio, a estrutura/metadata do formato custom produz o resultado contrário em ambos os códigos. Isso não demonstra corrupção do backup nem dispensou o teste separado de restauração real.
- **Busca:** `BuscarPorNomeAsync` continua interpolando o termo em `ILIKE '%termo%'` sem escapar `%` e `_`. O trecho responsável é idêntico nos dois códigos.
- **Orçamento:** `AtualizarAsync`/`AtualizarTransacionalAsync` não impedem edição do orçamento aprovado, embora a mudança de status já possua restrição. Serviço e repository são idênticos.
- **Venda externa:** a sequência é calculada lendo o último número e somando um antes do registro; chamadas concorrentes disputam o mesmo número. Serviço e repository são idênticos. A quantidade de colisões pode variar conforme o escalonamento; nesta comparação foi 2/5 em ambos.

Essas pendências devem ser tratadas em correções próprias. Não foi alterada regra de orçamento, busca ou numeração apenas para fechar a implementação v3.

## Evidências locais

Os artefatos abaixo ficam em diretórios ignorados pelo Git:

- `logs/catalog-v3-proof/baseline-migrate.log`
- `logs/catalog-v3-proof/current-migrate.log`
- `logs/catalog-v3-proof/baseline-four.log`
- `logs/catalog-v3-proof/current-four.log`
- `TestResults/catalog-v3-proof/baseline-four.trx`
- `TestResults/catalog-v3-proof/current-four.trx`
- `logs/catalog-v3-proof/paths.json` — caminhos das cópias e SHA-256 do archive.
- `logs/catalog-v3-proof/source-comparison.json`
- `logs/catalog-v3-proof/assertion-comparison.json`

SHA-256 do archive HEAD usado: `12FDB6CC3923F479A54993993B56AF34FC559124FA9E2A70577F1EA83ADDD44F`.
Os hashes abaixo normalizam **somente CRLF/LF** e coincidem entre baseline e cópia atual:

| Código comparado | SHA-256 comum |
| --- | --- |
| `OrcamentoService.cs` | `8C7A2E8D12C963C5B5562AD1D49E82D1DE94C283C09775226F3B4CC90D4A1C58` |
| `VendaExternaService.cs` | `14BDFE597CCA467B561C86D14B9CC8D73DD158C49C7FABCFC7C3512841E7F631` |
| `OrcamentoRepository.cs` | `A8A1882A6AC8B0C12FFE274719B72028CD1DF63668DC10582D2C8B0C11B6A692` |
| `VendaExternaRepository.cs` | `B82A0EABFC6590709766334030C8BA0F38BF1390A3099510CAF4057F12D4E8D3` |
| `BackupService.cs` | `CB19E573AE04F4411F82C55E04AB539737796561D3477F34318DBCA69A521382` |
| `ProdutoRepository.cs#BuscarPorNomeAsync` | `392EF3D88E8275B496F345D8C863D161FFF313A8BFFA62428827A954887C76E9` |

| Asserção/método original | SHA-256 comum |
| --- | --- |
| Busca com curinga | `BC4C2975114CDD73111DB2CCD0D5818FC68440B6A9226BAFA11176B3DB983C38` |
| Editar orçamento aprovado | `713260E119C68A1FFE49DB9CE4D263F162DBE2DC1D0F5193B851009C564EC45E` |
| Venda externa concorrente | `3086A530EB3DF9DD095CC9F5C6830698F5CBA3ABB65AD34CE11D6A07B7A69820` |
| Backup, desde a data fixa até as asserções finais | `DDB9A7929667F699EA1CAD831E76E958679699D0968F76A68C24D77FB3295D0C` |

Filtro xUnit utilizado nas duas cópias:

```text
FullyQualifiedName~BackupCompleto_GeraDumpComprimidoESqlConvertidoDele|FullyQualifiedName~BuscaDeProduto_ComCaractereCuringa_NaoDeveDevolverOCatalogoInteiro|FullyQualifiedName~EditarOrcamentoJaAprovado_DeveSerBloqueado|FullyQualifiedName~RegistrarVendasExternasSimultaneas_NenhumaDeveFalhar
```

Reexecução exige novos bancos descartáveis equivalentes, aplicação das migrations correspondentes, `RUN_INTEGRATION_TESTS=true`, conexão explícita validada e `DB_*` fictícias equivalentes para o teste anterior de backup. Nunca copiar/carregar `.env` operacional para reproduzir esta prova.