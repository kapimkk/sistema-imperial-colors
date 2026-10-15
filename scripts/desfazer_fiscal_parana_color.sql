-- =====================================================================
-- desfazer_fiscal_parana_color.sql
--
-- Volta os produtos ao estado em que estavam ANTES do
-- atualizar_fiscal_parana_color.sql, usando a tabela de copia de
-- seguranca que aquela execucao criou (bkp_trib_parana_color_...).
--
--   * Produto que NAO tinha linha fiscal: a linha criada e apagada.
--   * Produto que tinha: origem, CFOPs, CSOSN, CST do ICMS, CST do PIS e
--     CST do COFINS voltam ao valor antigo (o resto nao foi tocado).
--
-- ATENCAO: so desfaz o que existe na copia. Se alguem editou a
-- tributacao desses produtos DEPOIS da atualizacao, essas edicoes
-- tambem sao revertidas nos campos acima.
--
-- Uso (informe o nome da tabela de copia que apareceu na execucao):
--   psql ... -v bkp=bkp_trib_parana_color_20261015_154602 -f desfazer_fiscal_parana_color.sql
-- Para ver as copias existentes:
--   psql ... -c "\dt bkp_trib_parana_color_*"
--
-- Arquivo em ASCII puro de proposito (ver atualizar_fiscal_parana_color.sql).
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off

\if :{?bkp}
\else
  \echo ERRO: informe a tabela de copia, ex.: -v bkp=bkp_trib_parana_color_20261015_154602
  \quit
\endif

\if :{?teste}
\else
  BEGIN;
\endif

\echo
\echo === Desfazendo a partir de :bkp ===

SELECT set_config('desfazer.bkp', :'bkp', false);

DO $$
BEGIN
  IF to_regclass(current_setting('desfazer.bkp')) IS NULL THEN
    RAISE EXCEPTION 'Tabela de copia "%" nao encontrada. Nada foi alterado.', current_setting('desfazer.bkp');
  END IF;
END $$;

-- 1) Produtos que nao tinham linha: apaga a linha que foi criada.
WITH apagadas AS (
  DELETE FROM tributacao_produtos t
  USING :"bkp" b
  WHERE t.produto_id = b.bkp_produto_id
    AND NOT b.tinha_linha
  RETURNING 1
)
SELECT count(*) AS linhas_apagadas FROM apagadas;

-- 2) Produtos que tinham linha: devolve os valores antigos.
WITH restauradas AS (
  UPDATE tributacao_produtos t
     SET origem             = b.origem,
         csosn_icms         = b.csosn_icms,
         cst_icms           = b.cst_icms,
         cfop_dentro_estado = b.cfop_dentro_estado,
         cfop_fora_estado   = b.cfop_fora_estado,
         cst_pis            = b.cst_pis,
         cst_cofins         = b.cst_cofins,
         atualizado_em      = b.atualizado_em
    FROM :"bkp" b
   WHERE t.produto_id = b.bkp_produto_id
     AND b.tinha_linha
  RETURNING 1
)
SELECT count(*) AS linhas_restauradas FROM restauradas;

\if :{?teste}
  \echo (modo teste: transacao deixada aberta)
\else
  COMMIT;
  \echo
  \echo *** DESFEITO: produtos voltaram ao estado da copia. ***
\endif
