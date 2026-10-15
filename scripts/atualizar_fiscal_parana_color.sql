-- =====================================================================
-- atualizar_fiscal_parana_color.sql
--
-- Grava a tributacao padrao em TODOS os produtos ATIVOS da categoria
-- TINTA e marca PARANA COLOR:
--
--     Origem da mercadoria .............. 0 - Nacional
--     CFOP venda dentro do estado ....... 5102
--     CFOP venda fora do estado ......... 6102
--     CSOSN do ICMS ..................... 102
--     CST do PIS ........................ 08
--     CST do COFINS ..................... 08
--
-- O que o script FAZ e o que NAO faz
--   * So mexe nessas 6 colunas (e zera o CST do ICMS, que e excludente
--     com o CSOSN). NCM, CEST, aliquotas e o resto da tributacao de cada
--     produto NAO sao tocados. A origem anterior, se havia, e substituida
--     por 0 - Nacional (fica na copia de seguranca).
--   * Produto que ainda nao tem linha em tributacao_produtos ganha uma
--     (a tabela e 1 linha por produto, criada so quando alguem preenche
--     a tributacao). Por isso nao e um UPDATE puro: um UPDATE simples
--     pularia em silencio esses produtos.
--   * Roda tudo em uma transacao: qualquer erro desfaz tudo.
--   * Guarda copia do que existia numa tabela bkp_trib_parana_color_*.
--   * Pode rodar mais de uma vez: na segunda nao altera nada.
--
-- Uso (PowerShell):  veja GUIA_atualizar_fiscal_parana_color.md
--   ensaio (nao grava): ... -v ensaio=1 -f atualizar_fiscal_parana_color.sql
--   aplicar:            ...             -f atualizar_fiscal_parana_color.sql
--
-- Variaveis opcionais (-v):
--   categoria='TINTA'        nome da categoria (padrao TINTA)
--   marca='PARANA COLOR'     nome da marca     (padrao PARANA COLOR)
--   ensaio=1                 roda tudo e DESFAZ no final
--
-- Arquivo propositalmente em ASCII puro (sem acentos): o psql do Windows
-- pode usar outra codificacao e corromper o texto.
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off
\pset null '(vazio)'

\if :{?categoria}
\else
  \set categoria 'TINTA'
\endif

\if :{?marca}
\else
  \set marca 'PARANA COLOR'
\endif

-- "teste" e usado so pelo script de teste: ele ja abriu a transacao e
-- decide o fim. No uso normal esta variavel nao existe.
\if :{?teste}
\else
  BEGIN;
\endif

\echo
\echo === Atualizacao fiscal: categoria :categoria / marca :marca ===

-- Compara sem acento, sem diferenca de caixa e sem espaco nas pontas.
CREATE OR REPLACE FUNCTION pg_temp.norm(t text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT upper(translate(btrim(t),
    U&'\00C1\00C0\00C2\00C3\00C4\00C9\00C8\00CA\00CB\00CD\00CC\00CE\00CF\00D3\00D2\00D4\00D5\00D6\00DA\00D9\00DB\00DC\00C7\00E1\00E0\00E2\00E3\00E4\00E9\00E8\00EA\00EB\00ED\00EC\00EE\00EF\00F3\00F2\00F4\00F5\00F6\00FA\00F9\00FB\00FC\00E7',
    'AAAAAEEEEIIIIOOOOOUUUUCaaaaaeeeeiiiiooooouuuuc'))
$$;

-- ---------------------------------------------------------------------
-- 1) Conferencias de seguranca
-- ---------------------------------------------------------------------

-- CSOSN so vale no Simples Nacional / MEI. Em Regime Normal o ICMS usa
-- CST e gravar CSOSN geraria nota rejeitada. Sem parametro gravado, o
-- sistema assume Simples Nacional (padrao do programa).
DO $$
DECLARE
  regime text;
BEGIN
  SELECT valor_texto INTO regime
  FROM parametros_sistema
  WHERE chave = 'RegimeTributarioEmpresa';

  IF regime IS NULL THEN
    RAISE NOTICE 'Regime tributario nao gravado no banco: o sistema assume Simples Nacional.';
  ELSIF regime NOT IN ('SimplesNacional', 'Mei') THEN
    RAISE EXCEPTION 'Regime tributario da empresa e "%": CSOSN 102 so vale para Simples Nacional/MEI. Nada foi alterado.', regime;
  END IF;
END $$;

-- Produtos que serao atualizados.
DO $$ BEGIN IF to_regclass('pg_temp.alvo') IS NOT NULL THEN DROP TABLE pg_temp.alvo; END IF; END $$;
CREATE TEMP TABLE alvo ON COMMIT DROP AS
SELECT p.id AS produto_id
FROM produtos p
JOIN categorias c ON c.id = p.categoria_id
JOIN marcas m ON m.id = p.marca_id
WHERE p.ativo
  AND pg_temp.norm(c.nome) = pg_temp.norm(:'categoria')
  AND pg_temp.norm(m.nome) = pg_temp.norm(:'marca');

\echo
\echo '--- Marcas parecidas (confira se alguma ficou de fora sem querer) ---'
SELECT m.nome AS marca,
       count(p.id) FILTER (WHERE p.ativo) AS produtos_ativos,
       CASE WHEN pg_temp.norm(m.nome) = pg_temp.norm(:'marca')
            THEN 'ATUALIZADA' ELSE 'ignorada' END AS situacao
FROM marcas m
LEFT JOIN produtos p ON p.marca_id = m.id
WHERE pg_temp.norm(m.nome) LIKE '%' || pg_temp.norm(:'marca') || '%'
   OR pg_temp.norm(:'marca') LIKE '%' || pg_temp.norm(m.nome) || '%'
GROUP BY m.id, m.nome
ORDER BY m.nome;

\echo
\echo '--- O que sera alterado ---'
SELECT count(*)                                              AS produtos_alvo,
       count(*) FILTER (WHERE t.produto_id IS NOT NULL)      AS ja_tem_linha_fiscal,
       count(*) FILTER (WHERE t.produto_id IS NULL)          AS linha_fiscal_sera_criada,
       count(*) FILTER (WHERE t.cst_icms IS NOT NULL)        AS com_cst_icms_que_sera_zerado
FROM alvo a
LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM alvo) THEN
    RAISE EXCEPTION 'Nenhum produto ativo encontrado para essa categoria e marca. Confira os nomes. Nada foi alterado.';
  END IF;
END $$;

-- ---------------------------------------------------------------------
-- 2) Copia de seguranca do que existe hoje
--    tinha_linha = false  -> produto nao tinha tributacao (a linha criada
--                            e apagada pelo desfazer_fiscal_parana_color.sql)
--    So cria a copia se houver algo a alterar: rodar de novo um script ja
--    aplicado nao pode gerar uma copia "do estado ja corrigido", que
--    enganaria quem fosse desfazer depois.
-- ---------------------------------------------------------------------
SELECT count(*) > 0 AS ha_o_que_alterar
FROM alvo a
LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id
WHERE (t.origem, t.csosn_icms, t.cst_icms, t.cfop_dentro_estado, t.cfop_fora_estado, t.cst_pis, t.cst_cofins)
      IS DISTINCT FROM (0, '102', NULL, '5102', '6102', '08', '08') \gset

\if :ha_o_que_alterar
  SELECT 'bkp_trib_parana_color_' || to_char(clock_timestamp(), 'YYYYMMDD_HH24MISS') AS bkp \gset

  CREATE TABLE :"bkp" AS
  SELECT a.produto_id AS bkp_produto_id,
         (t.produto_id IS NOT NULL) AS tinha_linha,
         t.*
  FROM alvo a
  LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id;

  \echo
  \echo Copia de seguranca criada na tabela: :bkp
  \echo (anote este nome: e ele que o desfazer_fiscal_parana_color.sql usa)
\else
  \echo
  \echo Todos os produtos ja estao com os valores corretos. Nada a alterar.
\endif

-- ---------------------------------------------------------------------
-- 3) Gravacao
--    So grava quando o valor realmente muda (segunda execucao nao mexe
--    em nada, nem em atualizado_em).
-- ---------------------------------------------------------------------
\echo
\echo '--- Resultado ---'
WITH gravado AS (
  INSERT INTO tributacao_produtos AS t
         (produto_id, origem, csosn_icms, cst_icms,
          cfop_dentro_estado, cfop_fora_estado,
          cst_pis, cst_cofins, atualizado_em)
  SELECT a.produto_id, 0, '102', NULL,
         '5102', '6102',
         '08', '08', clock_timestamp()
  FROM alvo a
  ON CONFLICT (produto_id) DO UPDATE
     SET origem             = EXCLUDED.origem,
         csosn_icms         = EXCLUDED.csosn_icms,
         cst_icms           = NULL,
         cfop_dentro_estado = EXCLUDED.cfop_dentro_estado,
         cfop_fora_estado   = EXCLUDED.cfop_fora_estado,
         cst_pis            = EXCLUDED.cst_pis,
         cst_cofins         = EXCLUDED.cst_cofins,
         atualizado_em      = EXCLUDED.atualizado_em
   WHERE (t.origem, t.csosn_icms, t.cst_icms, t.cfop_dentro_estado, t.cfop_fora_estado, t.cst_pis, t.cst_cofins)
         IS DISTINCT FROM (0, '102', NULL, '5102', '6102', '08', '08')
  RETURNING (xmax = 0) AS criada
)
SELECT (SELECT count(*) FROM alvo)                        AS produtos_alvo,
       count(*) FILTER (WHERE criada)                     AS linhas_criadas,
       count(*) FILTER (WHERE NOT criada)                 AS linhas_atualizadas,
       (SELECT count(*) FROM alvo) - count(*)             AS ja_estavam_corretos
FROM gravado;

-- ---------------------------------------------------------------------
-- 4) Prova: todos os produtos alvo precisam estar com os 6 valores.
--    Se algum nao estiver, aborta e desfaz tudo.
-- ---------------------------------------------------------------------
DO $$
DECLARE
  fora_do_padrao integer;
BEGIN
  SELECT count(*) INTO fora_do_padrao
  FROM alvo a
  LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id
  WHERE t.produto_id IS NULL
     OR t.origem IS DISTINCT FROM 0
     OR t.csosn_icms IS DISTINCT FROM '102'
     OR t.cst_icms IS NOT NULL
     OR t.cfop_dentro_estado IS DISTINCT FROM '5102'
     OR t.cfop_fora_estado IS DISTINCT FROM '6102'
     OR t.cst_pis IS DISTINCT FROM '08'
     OR t.cst_cofins IS DISTINCT FROM '08';

  IF fora_do_padrao > 0 THEN
    RAISE EXCEPTION 'Conferencia final falhou: % produto(s) fora do padrao. Tudo foi desfeito.', fora_do_padrao;
  END IF;
END $$;

\echo
\echo '--- Conferencia: valores gravados nos produtos alvo ---'
SELECT t.origem, t.cfop_dentro_estado AS cfop_dentro, t.cfop_fora_estado AS cfop_fora,
       t.csosn_icms AS csosn, t.cst_pis AS pis, t.cst_cofins AS cofins,
       count(*) AS produtos
FROM alvo a
JOIN tributacao_produtos t ON t.produto_id = a.produto_id
GROUP BY 1, 2, 3, 4, 5, 6;

-- ---------------------------------------------------------------------
-- 5) Fim
-- ---------------------------------------------------------------------
\if :{?teste}
  \echo
  \echo '(modo teste: transacao deixada aberta para o script de teste)'
\elif :{?ensaio}
  ROLLBACK;
  \echo
  \echo '*** ENSAIO: tudo foi DESFEITO, nada foi gravado. ***'
\else
  COMMIT;
  \echo
  \echo '*** APLICADO: alteracoes gravadas. ***'
\endif
