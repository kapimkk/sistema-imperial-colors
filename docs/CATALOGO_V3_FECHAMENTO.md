# Fechamento do catálogo v3

Homologação local em 08/10/2026, somente em cópias/bases descartáveis. Sem commit, push,
deploy, alteração de secrets reais ou migration na base operacional.

## Quatro falhas Application

Backup, busca com curingas, orçamento aprovado e numeração concorrente de venda externa
foram reproduzidos no HEAD anterior e na cópia atual, preservando as asserções. Todas são
**A — preexistentes**, sem relação demonstrada com v3. A prova registra commit, hashes de
código/asserções, logs e TRX em [CATALOGO_V3_BASELINE_APPLICATION.md](CATALOGO_V3_BASELINE_APPLICATION.md).
A suíte completa atual executou **971 casos: 967 aprovados, essas quatro falhas, zero ignorados**.
Não se afirma 971/971 nem se alteram regras comerciais para esconder as falhas.

## Homologação WPF

**197/197 testes UI aprovados**, incluindo dez casos novos. Renderizações do XAML WPF real
com resolução de referência **1366×768**, recursos da aplicação e execução dos handlers cobrem:
produto novo; legado sem medidas; descrição; peso em kg e dimensões em cm; validação de
campos; preview; seleção; substituição; imagem inválida; remoção pendente até Salvar.

Nove capturas ficam ignoradas em `logs/catalog-v3-closure/wpf/`, de `01-novo-topo.png` a
`09-remocao-pendente.png`. Incluem scroll nas seções descrição/frete/imagem, sem depender de
banco ou credenciais reais. A fronteira `IProdutoFormDialogs` mantém o seletor Windows em
produção e permite respostas falsas controladas nos testes, executando os mesmos handlers.

Limite: o helper de controle nativo não ficou disponível nesta sessão. Portanto a seleção
no diálogo nativo não foi realizada manualmente; foi exercitada por fake na mesma fronteira.
As capturas representam a tela WPF real, não um mock HTML. Homologar o diálogo/ACL da máquina
real no cutover. Corrigidos recurso de cor de prontidão para frete e limite de altura conforme
área útil da tela. O host de testes foi ajustado para carregar os recursos reais sem startup
operacional/migrations.

## Multi-PC

O README confirma vários executáveis locais usando o mesmo PostgreSQL. Foi implementada
`PRODUCT_IMAGES_ROOT`, raiz absoluta/UNC comum contendo `ImagensProdutos`, configurada no
`.env` de cada PC e no `ImperialSync.env`. Configuração explícita inválida/indisponível não
faz fallback local. A referência do banco continua relativa, sem renomear SKU ou imagens.

Testes usam duas instalações distintas com uma raiz comum e conferem o mesmo arquivo/hash,
sem cópias locais divergentes. Isso valida o contrato; não equivale a dois PCs físicos/SMB
operacional homologados. No cutover, configurar ACL da LAN: C# lê/escreve; Sync somente lê;
conta do agendador acessa o UNC sem unidade mapeada. Credenciais SMB ficam no Windows.

## Backup e restauração real

BackupService inclui `.dump`, `.sql`, `ImagensProdutos` e manifesto SHA-256. Lock compartilhado
coordena imagem (preparação→commit→limpeza) e backup (dump→cópia), com timeout/cancelamento e
liberação do handle pelo SO após crash.

A prova cria duas bases PostgreSQL 18 descartáveis em loopback: migra a origem, grava produto
com PNG e dados físicos, executa BackupService, restaura com `pg_restore` em outra base e
restaura imagens em outro diretório. Confere `imagem_produto_path`, SHA-256, decodificação,
peso, três dimensões e descrição. As bases próprias são removidas ao final. Evidências locais:
`logs/catalog-shared-images-backup.log`, `TestResults/shared-images-backup.trx` e
`logs/catalog-v3-closure/application-final.log`.

Restauração pré-valida manifesto/refs/hash/conteúdo, copia para staging na mesma raiz,
revalida os bytes e promove o diretório completo somente após sucesso. Casos de erro de disco
e cancelamento após a primeira cópia confirmam limpeza, ausência de catálogo parcial e retry
bem-sucedido. Destino não vazio, corrupção e traversal são recusados.

O fallback de logos agora aceita somente assets de imagem e não percorre subpastas da raiz.
O teste real inclui `.env`, `ImperialSync.env`, executável, log e screenshot aninhado fictícios
e comprova que não entram no pacote como logos. Configuração/ACL e `.env` requerem backup
separado protegido; manter também cópia externa do pacote de banco e catálogo.

## Resultado e alterações adicionais

- Restore/build Release aprovados; **51/51 casos específicos** (40 frete/imagem + 1 migration + 10 compartilhamento/backup).
- **UI 197/197**; Application **967/971**, somente as quatro falhas A comprovadas.
- Legado sem medidas continua editável, preservando dados; prontidão para frete permanece falsa até completar peso e três dimensões válidos. A migration testa a gravação real de LEGACY1, mantendo SKU, estoque, descrição e campos físicos NULL.
- Adicionados raiz compartilhada, backup com manifesto/lock e restauração por staging; restringida cópia de logos para excluir arquivos sensíveis.
- Ajustados recurso de cor, altura útil WPF, host de testes e fronteira de diálogos; sem redesenho ou mudança de regra comercial.
- Corrigida fixture de códigos que reutilizava categoria/marca temporárias de outras suítes. Agora cria/limpa somente seus próprios registros; asserções originais preservadas.

Antes do uso real: homologar compartilhamento/ACL e seletor Windows na loja, configurar a mesma
raiz em todos os nós, copiar imagens existentes preservando nomes e testar recuperação do
pacote externo. Ver [procedimento de catálogo](CATALOGO_FRETE_IMAGENS_SYNC.md).
