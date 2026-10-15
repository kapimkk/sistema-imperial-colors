# Guia — Atualizar a tributação dos produtos TINTA / PARANA COLOR

Grava em **todos os produtos ativos** da categoria **TINTA** e marca **PARANA COLOR**:

| Campo | Valor |
|---|---|
| Origem da mercadoria | `0 - Nacional` |
| CFOP venda dentro do estado | `5102` |
| CFOP venda fora do estado | `6102` |
| CSOSN do ICMS | `102` |
| CST do PIS | `08` |
| CST do COFINS | `08` |

## O que o script faz (e não faz)

- **Só mexe nesses 6 campos.** NCM, CEST, alíquotas etc. de cada produto ficam como estão. Se algum produto já tinha outra origem (ex.: estrangeira), ela é **substituída** por `0 - Nacional` — o valor antigo fica na cópia de segurança.
- **Zera o CST do ICMS** dos produtos afetados: CST e CSOSN são excludentes (CSOSN é do Simples Nacional).
- Produto que **ainda não tem tributação cadastrada** ganha uma linha nova — por isso não é um `UPDATE` puro (um `UPDATE` simples pularia esses produtos em silêncio).
- Produtos **inativos**, de **outra categoria** ou de **outra marca** (inclusive marca parecida, como `PARANA COLOR 2 BD`) **não são tocados**.
- Roda numa **transação**: se algo falhar, nada é gravado.
- **Guarda cópia** do que existia numa tabela `bkp_trib_parana_color_AAAAMMDD_HHMMSS`.
- Pode ser rodado de novo sem problema: na segunda vez não altera nada.
- **Bloqueia** se o regime da empresa não for Simples Nacional/MEI (CSOSN só vale nesses regimes).

## Arquivos (pasta `scripts`)

| Arquivo | Para quê |
|---|---|
| `atualizar_fiscal_parana_color.sql` | O script que aplica |
| `desfazer_fiscal_parana_color.sql` | Volta ao estado anterior, usando a cópia |
| `testar_atualizar_fiscal_parana_color.sql` | Teste automatizado (não altera o banco) — só para o desenvolvedor |

## Passo a passo

> Rode no computador onde está o PostgreSQL (ou aponte `-h` para o IP dele).
> Faça fora do horário de uso, ou com ninguém salvando produtos.

### 1. Abrir o PowerShell e preparar

```powershell
cd C:\caminho\da\pasta\scripts

Set-Alias psql    "C:\Program Files\PostgreSQL\18\bin\psql.exe"
Set-Alias pg_dump "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
$env:PGPASSWORD = "SENHA_DO_POSTGRES"     # a mesma DB_PASSWORD do arquivo .env do sistema
```

> As duas linhas `Set-Alias` só valem **nesta janela** do PowerShell — se fechar e abrir outra, rode-as de novo.
> Elas existem porque o PostgreSQL não vem no PATH do Windows.

Banco, usuário e porta são os do `.env` do sistema (`DB_NAME`, `DB_USER`, `DB_PORT`). Nos comandos abaixo estão os padrões: `imperial_colors`, `postgres`, `5432`.

### 2. Backup completo do banco (recomendado)

```powershell
pg_dump -h localhost -p 5432 -U postgres -F c -f "C:\backup_antes_fiscal.dump" imperial_colors
```

### 3. ENSAIO — mostra o que faria e desfaz tudo

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v ensaio=1 -f atualizar_fiscal_parana_color.sql
```

Confira na saída:

- **Marcas parecidas**: a marca `PARANA COLOR` deve aparecer como `ATUALIZADA`. Se existir outra com o nome parecido (ex.: `PARANA COLOR 2 BD`) ela aparece como `ignorada` — veja o item *Outro nome de marca* abaixo se ela também precisa receber.
- **produtos_alvo**: quantos produtos serão atualizados.
- **linha_fiscal_sera_criada**: quantos ainda não tinham tributação.
- No final deve aparecer `ENSAIO: tudo foi DESFEITO`.

### 4. APLICAR

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -f atualizar_fiscal_parana_color.sql
```

No final: `APLICADO: alterações gravadas.` e a tabela de **conferência** com uma única linha:

```
 origem | cfop_dentro | cfop_fora | csosn | pis | cofins | produtos
      0  | 5102        | 6102      | 102   | 08  | 08     |  NNN
```

**Anote o nome da tabela de cópia** que aparece (`bkp_trib_parana_color_...`) — o desfazer usa.

### 5. Conferir no sistema

Abra o sistema → **Estoque** → dê dois cliques em um produto TINTA / PARANA COLOR → seção de tributação: Origem `0 - Nacional`, CFOPs `5102`/`6102`, CSOSN `102`, PIS e COFINS `08`.

## Se precisar DESFAZER

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v bkp=bkp_trib_parana_color_AAAAMMDD_HHMMSS -f desfazer_fiscal_parana_color.sql
```

Para listar as cópias existentes:

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -c "\dt bkp_trib_parana_color_*"
```

> Se rodou o script mais de uma vez, use a **cópia mais antiga**: é a que tem o estado original. (Rodar de novo, sem nada a alterar, não cria cópia nova.)

## Outro nome de marca / categoria

Se no banco a marca tem outro nome (por exemplo `PARANA COLOR 2 BD`), informe o nome exato:

```powershell
psql -h localhost -p 5432 -U postgres -d imperial_colors -v ensaio=1 -v marca="PARANA COLOR 2 BD" -f atualizar_fiscal_parana_color.sql
```

O mesmo vale para `-v categoria="..."`. A comparação ignora maiúsculas, acentos e espaços nas pontas.
Para atualizar duas marcas diferentes, rode o ensaio e o aplicar uma vez para cada nome.

## Mensagens de erro comuns

| Mensagem | Significa |
|---|---|
| `Regime tributario da empresa e "...": CSOSN 102 so vale para Simples Nacional/MEI` | A empresa não está no Simples. Nada foi alterado. |
| `Nenhum produto ativo encontrado para essa categoria e marca` | Nome digitado não confere. Veja a lista de marcas parecidas e use `-v marca=...`. |
| `password authentication failed` | Senha do `PGPASSWORD` errada. |
| `could not connect to server` | O PostgreSQL não está no `localhost`/porta informados. |
| `psql` não é reconhecido | As linhas `Set-Alias` do passo 1 não foram rodadas **nesta janela** do PowerShell. Rode-as de novo. |
| `Token '-h' inesperado na expressão ou instrução` | Chamou o programa por uma variável sem o `&` (ex.: `$psql -h ...`). Use o alias `psql` do passo 1, ou escreva `& $psql -h ...`. |
