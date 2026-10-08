# Cadastro da loja → ImperialSync → site: frete, descrição e imagem

## Banco e formulário

A migration aditiva **20261007203618_AddProdutoFreteImagem** mantém codigo_interno,
peso_gramas e observacoes. Acrescenta:

| Coluna | Tipo / finalidade |
| --- | --- |
| altura_cm | numeric(10,2) NULL, cm |
| largura_cm | numeric(10,2) NULL, cm |
| comprimento_cm | numeric(10,2) NULL, cm |
| imagem_produto_path | varchar(200) NULL, referência relativa gerada |
| imagem_removida | boolean NOT NULL DEFAULT false, remoção explícita |

CHECKs exigem dimensões positivas quando informadas e referência nula se a imagem
foi explicitamente removida. Os 222 cadastros fictícios da prova de migration mantiveram
estoque, descrição e campos novos nulos. Nenhum dado antigo recebe medidas inventadas.

Novos produtos exigem peso e as três dimensões. Produtos existentes podem salvar
edições mantendo dados ainda não informados; o formulário mostra **Dados de frete
pendentes**. Valor preenchido inválido é recusado. **Pronto para frete** depende dos
quatro campos válidos.

Peso aparece em kg com até três casas decimais: 5,500 kg vira exatamente 5500
em peso_gramas. Dimensões usam cm, duas casas decimais, decimal no C#.
Não há limite fictício de transportadora: o teto do peso é a capacidade técnica do
int32; a Frenet e a transportadora continuam decidindo quais encomendas aceitam.

observacoes continua sendo a mesma coluna. O label é **Descrição / Observações**,
com até 10.000 caracteres, preservando quebras de linha. Texto longo é recusado com
mensagem, nunca truncado ao salvar. HTML permanece texto: quem exibe no site deve
escapar/renderizar como texto, sem interpretar HTML arbitrário.

## Imagem principal

Instalação única: arquivos ficam em Path.Combine(AppContext.BaseDirectory, "ImagensProdutos").
Multi-PC: `PRODUCT_IMAGES_ROOT` define uma raiz absoluta local/UNC contendo a subpasta
`ImagensProdutos`; a mesma raiz deve ser configurada em TODOS os `.env` e no `ImperialSync.env`.
A configuração explícita inacessível/relativa é recusada, sem fallback para a instalação local.
O banco guarda somente ImagensProdutos/(GUID de 32 caracteres).jpg ou .png.
SKU e nome original do arquivo nunca formam o nome persistido.

O formulário aceita JPEG/PNG, até **5.000.000 bytes**, no máximo 8000 pixels por lado
e 40 megapixels, os mesmos limites de upload do site. WebP é suportado no site,
mas não é aceito no formulário C# nesta etapa porque a pré-visualização WPF não
dispõe de decoder WebP confiável em todas as instalações.

Extensão, assinatura, decodificação, tamanho e dimensões são verificados. Caminhos
persistidos externos, traversal e links/reparse points são recusados. A cópia tem
nome exclusivo e é revalidada antes da gravação no banco. Arquivo grande durante a
cópia também é interrompido.

Substituição prepara arquivo novo, confirma a transação do produto e só então tenta
remover o anterior. Rollback limpa somente o novo. Remoção no formulário é apenas
uma intenção até **Salvar** confirmar o banco. Arquivo ausente mantém a referência
e exibe pendência; nunca vira ordem automática para apagar imagens do site.

Edição aberta antes de outra substituição preserva a imagem atual se não houve
alteração explícita da imagem. Substituições simultâneas podem deixar arquivo órfão
recuperável, mas não devem apagar o arquivo atualmente referenciado; limpeza de
órfãos exige conferência posterior de todas as referências, sem exclusão cega.

## Autoridade e integração

A loja passa a ser autoridade de peso, dimensões, descrição de observacoes e imagem
principal importada. ImperialSync usa o contrato versionado v3 e upload separado,
sem blob no PostgreSQL e sem Base64 no snapshot. SEO, destaque, publicação/draft e
imagens adicionais continuam pertencendo ao site.

### Instalações em vários computadores

O README do sistema documenta instalações locais independentes (balcão/PDV/escritório)
conectadas ao mesmo PostgreSQL. O banco compartilhado não compartilha arquivos; portanto a
pasta padrão local só é adequada a uma instalação única. Não usar replicação manual como
solução operacional para múltiplos PCs.

1. No servidor já existente da loja, criar uma pasta de catálogo e compartilhá-la por SMB,
   acessível apenas às contas Windows dos operadores; nunca publicar SMB na internet.
2. Configurar `PRODUCT_IMAGES_ROOT=\\SERVIDOR\ImperialCatalogo` (exemplo) no `.env` de cada
   ImperialColors e no `ImperialSync.env`. Essa raiz contém `ImagensProdutos`, criada pelo sistema.
   Usar o mesmo UNC também no servidor evita configurações locais divergentes.
3. Copiar imagens atuais para essa raiz preservando nomes e referências; fazê-lo com cadastro e
   Sync parados. Não mover o banco nem alterar `imagem_produto_path`.
4. Operadores C# precisam de leitura/criação/substituição/remoção; a conta que executa o Sync
   precisa apenas de leitura. Credenciais SMB ficam no Windows, nunca no banco ou payload Sync.
5. Testar no PC A: selecionar/salvar imagem; no PC B: abrir preview; no nó Sync: conferir upload.
   Conta do Agendador de Tarefas deve ter acesso ao UNC sem depender de uma unidade mapeada.

Não exige servidor novo: utiliza o servidor/LAN documentados no sistema. Se o compartilhamento
estiver indisponível, não criar uma pasta alternativa nem sobrescrever a referência. O Sync
preserva imagens já existentes no site; a indisponibilidade não é uma ordem de remoção.
Os testes simulam duas instalações locais distintas usando a mesma raiz e conferem referência,
conteúdo e ausência de cópias locais. Homologar ACL/SMB real na rede da loja antes do cutover.

## Backup e restauração

**O dump do PostgreSQL não contém as imagens.** O `BackupService` agora inclui automaticamente
`ImagensProdutos/` e `imagens_produtos_manifest.json` (referência, tamanho e SHA-256), além do
`.dump`, `.sql`, logos e configuração já existentes. A cópia de logos aceita somente extensões
de imagem; quando a pasta de logos é a raiz da instalação, não percorre subpastas. `.env`,
`ImperialSync.env`, executáveis e logs não são incluídos como assets. Links externos são recusados.

Edições explícitas de imagem e o backup usam o mesmo arquivo de bloqueio na raiz compartilhada,
com exclusão de acesso do Windows/SMB. O backup mantém o bloqueio desde antes do `pg_dump` até
terminar a cópia; a edição mantém desde antes da leitura/preparação até commit/limpeza. Assim
uma imagem referenciada pelo snapshot não é removida antes da cópia. O SO libera o handle após
crash; disputa espera até 30 segundos, aceita cancelamento e falha sem marcar backup concluído.
Não excluir `.imperial-catalog.lock` durante uma operação; a presença do arquivo não indica lock
ativo, pois a exclusão depende do handle aberto.

Restaurar com sistema/Sync parados: banco em base vazia e pasta de imagens em raiz vazia,
preservando exatamente os nomes relativos. `BackupImagensProdutos.RestaurarAsync` confere todo
o manifesto, hashes, bytes, formato e traversal antes de copiar; recusa destino não vazio.
Copia primeiro para staging irmão na mesma raiz, revalida os bytes copiados e só então publica
com rename do diretório completo. Cancelamento/falha de disco limpa o staging e não deixa um
catálogo parcialmente restaurado. Testes falham após a primeira cópia e confirmam retry seguro.
O teste real cria duas bases PostgreSQL descartáveis, migra a origem, grava um produto com PNG,
executa o BackupService, restaura com `pg_restore` na segunda base e as imagens em outra raiz.
Confere `imagem_produto_path`, peso/medidas/descrição, SHA-256 idêntico e decodificação da imagem.
Também testa corrupção e manifesto perigoso sem escrever fora do destino.

O pacote diário precisa continuar sendo copiado para mídia/local externo protegido. Nunca apague
`ImagensProdutos` durante atualização ou publish: contém dados, não cache. Configuração/ACL do
compartilhamento e `.env` devem ter backup separado protegido pelo responsável da loja.

## Migration e validação local

Não iniciar o sistema contra o banco real para homologação: o startup aplica
migrations automaticamente. Primeiro valide uma cópia e faça backup completo.

Ferramentas EF permitem IMPERIAL_DESIGN_TIME_CONNECTION_STRING explícita para
geração/teste sem carregar .env. Integrações usam RUN_INTEGRATION_TESTS=true
com IMPERIAL_TEST_DATABASE_CONNECTION_STRING, exclusivamente host loopback e
banco com sufixo _test. Testes não leem credenciais da instalação.

A prova específica de migration exige CATALOG_STORE_TEST_CONNECTION_STRING para
a fixture loopback 5438, banco-base imperial_csharp_v3_test; cria seu próprio
banco fictício, aplica schema anterior, cadastra 222 legados e aplica a migration
nova. Inclui CRUD, imagem, constraints, preservação de texto/estoque e edição
concorrente sem restaurar imagem antiga.

## Pendências constatadas nas suítes antigas

A execução acumulada com integrações habilitadas em banco isolado encontrou quatro
falhas comprovadamente anteriores ao catálogo v3 (classificação A), reproduzidas no
HEAD anterior e na cópia atual com as mesmas asserções e PostgreSQL isolado. A
[evidência objetiva da comparação](CATALOGO_V3_BASELINE_APPLICATION.md) registra hashes,
TRX e resultados de ambos. São: hipótese de dump comprimido sempre ser
menor que SQL em banco vazio/pequeno; busca tratando %_ como curingas; edição de
orçamento aprovado; numeração concorrente de venda externa. Esses testes não foram
afrouxados nem rotinas comerciais reescritas nesta evolução do catálogo.

A primeira execução de suíte encontrou um teste legado de login que carregava
.env sem guard. Seu fluxo pode inserir evento LOGIN_SUCESSO no banco configurado.
O teste foi corrigido para banco local explicitamente isolado e usuário fictício;
não houve tentativa de consultar, apagar ou restaurar registros no banco da instalação.
A execução posterior de todas as integrações usou somente a fixture local criada.

O usuário confirmou que o banco configurado nesse .env é uma cópia de desenvolvimento,
não o banco operacional do cliente. A proteção dos testes permanece corrigida para
que nenhuma suíte dependa dessa configuração local implícita.

Resultado do fechamento: restore e build Release aprovados; **51 casos específicos**
aprovados (40 de frete/imagem, 1 de migration e 10 de imagens compartilhadas/backup);
**UI 197/197**. Application com todas as integrações habilitadas na fixture isolada:
**971 executados, 967 aprovados, quatro falhas preexistentes, zero ignorados**. Nenhuma
asserção foi afrouxada. A comparação com o HEAD está em
[CATALOGO_V3_BASELINE_APPLICATION.md](CATALOGO_V3_BASELINE_APPLICATION.md); a homologação
visual e recuperação estão em [CATALOGO_V3_FECHAMENTO.md](CATALOGO_V3_FECHAMENTO.md).
O diff check passou. Não houve commit ou push.
