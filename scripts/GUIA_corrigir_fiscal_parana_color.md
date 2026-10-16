# Guia — Corrigir a tributação dos produtos PARANA COLOR (TINTA e REVESTIMENTO)

Corrige os produtos **ativos** da marca **PARANA COLOR**, com uma regra por categoria:

| Campo | **TINTA** | **REVESTIMENTO** |
|---|---|---|
| Origem da mercadoria | `0 - Nacional` | `0 - Nacional` |
| CFOP venda dentro do estado | `5403` | `5102` |
| CFOP venda fora do estado | `6403` | `6102` |
| CSOSN do ICMS | `500` | `102` |
| CST do PIS | *(não mexe)* | `08` |
| CST do COFINS | *(não mexe)* | `08` |

## O que o script faz (e não faz)

- **Só grava o que está na tabela acima.** Campo que a regra não cita **fica como está**. Em TINTA, por exemplo, **PIS e COFINS não são alterados** (se já estão `08` por causa do script anterior, continuam assim).
- Se algum produto já tinha **outra origem** (ex.: estrangeira), ela é **substituída** por `0 - Nacional` — o valor antigo fica na cópia de segurança.
- NCM, CEST, alíquotas, pST do ICMS-ST etc. **não são tocados**.
- **Zera o CST do ICMS** dos produtos afetados: CST e CSOSN são excludentes (CSOSN é do Simples Nacional).
- Produto que **ainda não tem tributação cadastrada** ganha uma linha nova, só com os campos da regra — por isso não é um `UPDATE` puro (um `UPDATE` simples pularia esses produtos em silêncio).
- Produtos **inativos**, de **outra categoria** (ex.: ACESSÓRIOS) ou de **outra marca** (inclusive marca parecida, como `PARANA COLOR 2 BD`) **não são tocados**.
- Roda numa **transação**: se algo falhar, nada é gravado.
- **Guarda cópia** do que existia numa tabela `bkp_trib_corrigir_parana_color_AAAAMMDD_HHMMSS`.
- Pode ser rodado de novo sem problema: na segunda vez não altera nada e não cria cópia nova.
- **Bloqueia** se o regime da empresa não for Simples Nacional/MEI (CSOSN só vale nesses regimes).

> ⚠️ **CSOSN 500** é "ICMS cobrado anteriormente por substituição tributária". O percentual de ST retido (pST) **não é definido por este script** — fica como está no cadastro de cada produto. Se a nota exigir, preencha no cadastro.

## Arquivos (pasta `scripts`)

| Arquivo | Para quê |
|---|---|
| `corrigir_fiscal_parana_color.sql` | O script que aplica a correção |
| `desfazer_fiscal_parana_color.sql` | Volta ao estado anterior, usando a cópia |
| `testar_corrigir_fiscal_parana_color.sql` | Teste automatizado (não altera o banco) — só para o desenvolvedor |

## Passo a passo

> Rode no computador onde está o PostgreSQL (ou aponte `-h` para o IP dele).
> Faça fora do horário de uso, ou com ninguém salvando produtos.

### 1. Abrir o PowerShell e preparar

```powershell
Set-Alias psql    "C:\Program Files\PostgreSQL\18\bin\psql.exe"
Set-Alias pg_dump "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"

$env:PGPASSWORD = "SENHA_DO_POSTGRES"     # a mesma DB_PASSWORD do arquivo .env do sistema
cd C:\caminho\da\pasta\scripts
```

> As linhas `Set-Alias` só valem **nesta janela** do PowerShell — se fechar e abrir outra, rode-as de novo.
> Banco, usuário e porta são os do `.env` do sistema (`DB_NAME`, `DB_USER`, `DB_PORT`); abaixo estão os padrões.

### 2. Backup completo do banco (recomendado)

```powershell
pg_dump -h localhost -p 5432 -U postgres -F c -f "C:\backup_antes_correcao_fiscal.dump" imperial_colors
```

### 3. ENSAIO — mostra o que faria e desfaz tudo

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v ensaio=1 -f corrigir_fiscal_parana_color.sql
```

Confira na saída:

- **Marcas parecidas**: `PARANA COLOR` deve aparecer como `CORRIGIDA`. Se existir outra com nome parecido (ex.: `PARANA COLOR 2 BD`) ela aparece como `ignorada` — veja *Outro nome de marca* abaixo.
- **O que será alterado, por categoria**: uma linha para `TINTA` e outra para `REVESTIMENTO`, com quantos produtos cada uma tem, quantos terão a linha fiscal criada e quantos serão de fato alterados (`a_alterar`).
- **Conferência** no final: uma linha por categoria, com os valores da tabela deste guia.
- A última linha deve ser `ENSAIO: tudo foi DESFEITO`.

### 4. APLICAR

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -f corrigir_fiscal_parana_color.sql
```

No final: `APLICADO: alteracoes gravadas.`

**Anote o nome da tabela de cópia** que aparece (`bkp_trib_corrigir_parana_color_...`) — o desfazer usa.

### 5. Conferir no sistema

Abra o sistema → **Estoque** → dê dois cliques em um produto TINTA e em um REVESTIMENTO da PARANA COLOR → seção de tributação, e confira a origem, os CFOPs e o CSOSN da tabela acima.

## Se precisar DESFAZER

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v bkp=bkp_trib_corrigir_parana_color_AAAAMMDD_HHMMSS -f desfazer_fiscal_parana_color.sql
```

Para listar as cópias existentes:

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -c "\dt bkp_trib_*"
```

> Se rodou o script mais de uma vez com alterações, use a **cópia mais antiga**: é a que tem o estado original. (Rodar de novo sem nada a alterar não cria cópia.)

## Outro nome de marca / categoria

Se no banco os nomes forem diferentes, informe o nome exato (a comparação ignora maiúsculas, acentos e espaços nas pontas):

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v ensaio=1 -v marca="PARANA COLOR 2 BD" -f corrigir_fiscal_parana_color.sql
```

Também aceita `-v categoria1="..."` (a regra de TINTA) e `-v categoria2="..."` (a regra de REVESTIMENTO).

## Mensagens de erro comuns

| Mensagem | Significa |
|---|---|
| `Regime tributario da empresa e "...": CSOSN so vale para Simples Nacional/MEI` | A empresa não está no Simples. Nada foi alterado. |
| `Nenhum produto ativo encontrado para essa marca e essas categorias` | Os nomes não conferem. Veja a lista de marcas parecidas e use `-v marca=...`. |
| `password authentication failed` | Senha do `PGPASSWORD` errada. |
| `could not connect to server` | O PostgreSQL não está no `localhost`/porta informados. |
| `psql` não é reconhecido | As linhas `Set-Alias` do passo 1 não foram rodadas **nesta janela** do PowerShell. |
| `Token '-h' inesperado na expressão ou instrução` | Chamou o programa por uma variável sem o `&`. Use o alias `psql` do passo 1. |
