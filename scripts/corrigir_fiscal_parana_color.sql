-- =====================================================================
-- corrigir_fiscal_parana_color.sql
--
-- Correcao da tributacao dos produtos ATIVOS da marca PARANA COLOR,
-- com regra diferente por categoria:
--
--   TINTA         Origem da mercadoria ... 0 - Nacional
--                 CFOP dentro do estado ... 5403
--                 CFOP fora do estado ..... 6403
--                 CSOSN do ICMS ........... 500
--
--   REVESTIMENTO  Origem da mercadoria ... 0 - Nacional
--                 CFOP dentro do estado ... 5102
--                 CFOP fora do estado ..... 6102
--                 CSOSN do ICMS ........... 102
--                 CST do PIS .............. 08
--                 CST do COFINS ........... 08
--
-- Regra de ouro: so grava o que esta na lista acima.
--   * Campo que a regra NAO cita fica como esta. Em TINTA, por exemplo,
--     CST do PIS/COFINS NAO sao alterados (se ja estavam 08 por causa do
--     script anterior, continuam assim).
--   * A origem anterior, se havia (ex.: estrangeira), e substituida por
--     0 - Nacional. O valor antigo fica na copia de seguranca.
--   * NCM, CEST, aliquotas, pST do ICMS-ST e o resto da tributacao
--     tambem nao sao tocados.
--   * Zera o CST do ICMS dos produtos afetados (excludente com CSOSN).
--   * Produto que ainda nao tem linha em tributacao_produtos ganha uma,
--     so com os campos da regra (um UPDATE puro pularia esses produtos).
--
-- Seguranca:
--   * Tudo em uma transacao: qualquer erro desfaz tudo.
--   * Bloqueia se o regime da empresa nao for Simples Nacional / MEI.
--   * Copia do que existia em bkp_trib_corrigir_parana_color_*, criada so
--     quando ha o que alterar. Para voltar: desfazer_fiscal_parana_color.sql
--   * Pode rodar mais de uma vez: na segunda nao altera nada.
--
-- Uso (PowerShell): veja GUIA_corrigir_fiscal_parana_color.md
--   ensaio (nao grava): ... -v ensaio=1 -f corrigir_fiscal_parana_color.sql
--   aplicar:            ...             -f corrigir_fiscal_parana_color.sql
--
-- Variaveis opcionais (-v):
--   marca='PARANA COLOR'        nome da marca (padrao PARANA COLOR)
--   categoria1='TINTA'          categoria da 1a regra
--   categoria2='REVESTIMENTO'   categoria da 2a regra
--   ensaio=1                    roda tudo e DESFAZ no final
--
-- Arquivo em ASCII puro de proposito (o psql do Windows pode usar outra
-- codificacao e corromper acentos).
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off
\pset null '(vazio)'

\if :{?marca}
\else
  \set marca 'PARANA COLOR'
\endif
\if :{?categoria1}
\else
  \set categoria1 'TINTA'
\endif
\if :{?categoria2}
\else
  \set categoria2 'REVESTIMENTO'
\endif

-- "teste" e usado so pelo script de teste, que ja abriu a transacao e
-- decide o fim. No uso normal esta variavel nao existe.
\if :{?teste}
\else
  BEGIN;
\endif

\echo
\echo === Correcao fiscal: marca :marca / categorias :categoria1 e :categoria2 ===

-- Compara sem acento, sem diferenca de caixa e sem espaco nas pontas.
CREATE OR REPLACE FUNCTION pg_temp.norm(t text) RETURNS text
LANGUAGE sql IMMUTABLE AS $$
  SELECT upper(translate(btrim(t),
    U&'\00C1\00C0\00C2\00C3\00C4\00C9\00C8\00CA\00CB\00CD\00CC\00CE\00CF\00D3\00D2\00D4\00D5\00D6\00DA\00D9\00DB\00DC\00C7\00E1\00E0\00E2\00E3\00E4\00E9\00E8\00EA\00EB\00ED\00EC\00EE\00EF\00F3\00F2\00F4\00F5\00F6\00FA\00F9\00FB\00FC\00E7',
    'AAAAAEEEEIIIIOOOOOUUUUCaaaaaeeeeiiiiooooouuuuc'))
$$;

-- ---------------------------------------------------------------------
-- 1) Regras (NULL = "nao mexer neste campo")
-- ---------------------------------------------------------------------
DO $$ BEGIN IF to_regclass('pg_temp.regras') IS NOT NULL THEN DROP TABLE pg_temp.regras; END IF; END $$;
CREATE TEMP TABLE regras (
  categoria    text,
  origem       integer,
  cfop_dentro  text,
  cfop_fora    text,
  csosn        text,
  cst_pis      text,
  cst_cofins   text
) ON COMMIT DROP;

INSERT INTO regras (categoria, origem, cfop_dentro, cfop_fora, csosn, cst_pis, cst_cofins)
VALUES (:'categoria1', 0, '5403', '6403', '500', NULL, NULL),
       (:'categoria2', 0, '5102', '6102', '102', '08', '08');

-- ---------------------------------------------------------------------
-- 2) Conferencias de seguranca
-- ---------------------------------------------------------------------

-- CSOSN so vale no Simples Nacional / MEI. Sem parametro gravado, o
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
    RAISE EXCEPTION 'Regime tributario da empresa e "%": CSOSN so vale para Simples Nacional/MEI. Nada foi alterado.', regime;
  END IF;
END $$;

-- Produtos que serao corrigidos, ja com a regra da categoria de cada um.
DO $$ BEGIN IF to_regclass('pg_temp.alvo') IS NOT NULL THEN DROP TABLE pg_temp.alvo; END IF; END $$;
CREATE TEMP TABLE alvo ON COMMIT DROP AS
SELECT p.id AS produto_id,
       r.categoria AS categoria_regra,
       r.origem,
       r.cfop_dentro, r.cfop_fora, r.csosn, r.cst_pis, r.cst_cofins
FROM produtos p
JOIN categorias c ON c.id = p.categoria_id
JOIN marcas m ON m.id = p.marca_id
JOIN regras r ON pg_temp.norm(c.nome) = pg_temp.norm(r.categoria)
WHERE p.ativo
  AND pg_temp.norm(m.nome) = pg_temp.norm(:'marca');

\echo
\echo '--- Marcas parecidas (confira se alguma ficou de fora sem querer) ---'
SELECT m.nome AS marca,
       count(p.id) FILTER (WHERE p.ativo) AS produtos_ativos,
       CASE WHEN pg_temp.norm(m.nome) = pg_temp.norm(:'marca')
            THEN 'CORRIGIDA' ELSE 'ignorada' END AS situacao
FROM marcas m
LEFT JOIN produtos p ON p.marca_id = m.id
WHERE pg_temp.norm(m.nome) LIKE '%' || pg_temp.norm(:'marca') || '%'
   OR pg_temp.norm(:'marca') LIKE '%' || pg_temp.norm(m.nome) || '%'
GROUP BY m.id, m.nome
ORDER BY m.nome;

\echo
\echo '--- O que sera alterado, por categoria ---'
SELECT r.categoria,
       count(a.produto_id)                                          AS produtos,
       count(*) FILTER (WHERE a.produto_id IS NOT NULL AND t.produto_id IS NULL) AS linha_fiscal_sera_criada,
       count(*) FILTER (WHERE a.produto_id IS NOT NULL
         AND (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms, t.cst_icms, t.cst_pis, t.cst_cofins)
             IS DISTINCT FROM (COALESCE(a.origem, t.origem),
                               COALESCE(a.cfop_dentro, t.cfop_dentro_estado),
                               COALESCE(a.cfop_fora,   t.cfop_fora_estado),
                               a.csosn, NULL,
                               COALESCE(a.cst_pis,    t.cst_pis),
                               COALESCE(a.cst_cofins, t.cst_cofins)))   AS a_alterar,
       count(*) FILTER (WHERE t.cst_icms IS NOT NULL)               AS com_cst_icms_que_sera_zerado
FROM regras r
LEFT JOIN alvo a ON a.categoria_regra = r.categoria
LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id
GROUP BY r.categoria
ORDER BY r.categoria;

DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM alvo) THEN
    RAISE EXCEPTION 'Nenhum produto ativo encontrado para essa marca e essas categorias. Confira os nomes. Nada foi alterado.';
  END IF;
END $$;

-- ---------------------------------------------------------------------
-- 3) Copia de seguranca (so se houver algo a alterar)
--    tinha_linha = false  -> produto nao tinha tributacao (o desfazer
--                            apaga a linha criada)
-- ---------------------------------------------------------------------
SELECT count(*) > 0 AS ha_o_que_alterar
FROM alvo a
LEFT JOIN tributacao_produtos t ON t.produto_id = a.produto_id
WHERE (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms, t.cst_icms, t.cst_pis, t.cst_cofins)
      IS DISTINCT FROM (COALESCE(a.origem, t.origem),
                        COALESCE(a.cfop_dentro, t.cfop_dentro_estado),
                        COALESCE(a.cfop_fora,   t.cfop_fora_estado),
                        a.csosn, NULL,
                        COALESCE(a.cst_pis,    t.cst_pis),
                        COALESCE(a.cst_cofins, t.cst_cofins)) \gset

\if :ha_o_que_alterar
  SELECT 'bkp_trib_corrigir_parana_color_' || to_char(clock_timestamp(), 'YYYYMMDD_HH24MISS') AS bkp \gset

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
  \echo Todos os produtos ja estao corretos. Nada a alterar.
\endif

-- ---------------------------------------------------------------------
-- 4) Gravacao: so os campos da regra. Campo nulo na regra mantem o valor
--    atual (COALESCE). So grava quando algo realmente muda.
-- ---------------------------------------------------------------------
\echo
\echo '--- Resultado ---'
WITH gravado AS (
  INSERT INTO tributacao_produtos AS t
         (produto_id, origem, cfop_dentro_estado, cfop_fora_estado, csosn_icms, cst_icms,
          cst_pis, cst_cofins, atualizado_em)
  SELECT a.produto_id, a.origem, a.cfop_dentro, a.cfop_fora, a.csosn, NULL,
         a.cst_pis, a.cst_cofins, clock_timestamp()
  FROM alvo a
  ON CONFLICT (produto_id) DO UPDATE
     SET origem             = COALESCE(EXCLUDED.origem, t.origem),
         cfop_dentro_estado = COALESCE(EXCLUDED.cfop_dentro_estado, t.cfop_dentro_estado),
         cfop_fora_estado   = COALESCE(EXCLUDED.cfop_fora_estado,   t.cfop_fora_estado),
         csosn_icms         = EXCLUDED.csosn_icms,
         cst_icms           = NULL,
         cst_pis            = COALESCE(EXCLUDED.cst_pis,    t.cst_pis),
         cst_cofins         = COALESCE(EXCLUDED.cst_cofins, t.cst_cofins),
         atualizado_em      = EXCLUDED.atualizado_em
   WHERE (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms, t.cst_icms, t.cst_pis, t.cst_cofins)
         IS DISTINCT FROM (COALESCE(EXCLUDED.origem, t.origem),
                           COALESCE(EXCLUDED.cfop_dentro_estado, t.cfop_dentro_estado),
                           COALESCE(EXCLUDED.cfop_fora_estado,   t.cfop_fora_estado),
                           EXCLUDED.csosn_icms, NULL,
                           COALESCE(EXCLUDED.cst_pis,    t.cst_pis),
                           COALESCE(EXCLUDED.cst_cofins, t.cst_cofins))
  RETURNING (xmax = 0) AS criada
)
SELECT (SELECT count(*) FROM alvo)            AS produtos_alvo,
       count(*) FILTER (WHERE criada)         AS linhas_criadas,
       count(*) FILTER (WHERE NOT criada)     AS linhas_atualizadas,
       (SELECT count(*) FROM alvo) - count(*) AS ja_estavam_corretos
FROM gravado;

-- ---------------------------------------------------------------------
-- 5) Prova: cada produto alvo precisa estar com os valores da SUA regra.
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
     OR t.csosn_icms IS DISTINCT FROM a.csosn
     OR t.cst_icms IS NOT NULL
     OR (a.origem      IS NOT NULL AND t.origem             IS DISTINCT FROM a.origem)
     OR (a.cfop_dentro IS NOT NULL AND t.cfop_dentro_estado IS DISTINCT FROM a.cfop_dentro)
     OR (a.cfop_fora   IS NOT NULL AND t.cfop_fora_estado   IS DISTINCT FROM a.cfop_fora)
     OR (a.cst_pis     IS NOT NULL AND t.cst_pis            IS DISTINCT FROM a.cst_pis)
     OR (a.cst_cofins  IS NOT NULL AND t.cst_cofins         IS DISTINCT FROM a.cst_cofins);

  IF fora_do_padrao > 0 THEN
    RAISE EXCEPTION 'Conferencia final falhou: % produto(s) fora do padrao. Tudo foi desfeito.', fora_do_padrao;
  END IF;
END $$;

\echo
\echo '--- Conferencia: valores gravados nos produtos alvo ---'
SELECT a.categoria_regra AS categoria, t.origem,
       t.cfop_dentro_estado AS cfop_dentro, t.cfop_fora_estado AS cfop_fora,
       t.csosn_icms AS csosn, t.cst_pis AS pis, t.cst_cofins AS cofins,
       count(*) AS produtos
FROM alvo a
JOIN tributacao_produtos t ON t.produto_id = a.produto_id
GROUP BY 1, 2, 3, 4, 5, 6, 7
ORDER BY 1, 8 DESC;

-- ---------------------------------------------------------------------
-- 6) Fim
-- ---------------------------------------------------------------------
\if :{?teste}
  \echo
  \echo (modo teste: transacao deixada aberta para o script de teste)
\elif :{?ensaio}
  ROLLBACK;
  \echo
  \echo *** ENSAIO: tudo foi DESFEITO, nada foi gravado. ***
\else
  COMMIT;
  \echo
  \echo *** APLICADO: alteracoes gravadas. ***
\endif
