-- =====================================================================
-- testar_corrigir_fiscal_parana_color.sql
--
-- Teste automatizado do corrigir_fiscal_parana_color.sql.
--
-- NAO ALTERA O BANCO: cria categorias, marcas e produtos de mentira com
-- nomes "ZZ TESTE ...", roda o script de verdade sobre eles e confere o
-- resultado. Tudo numa transacao que termina em ROLLBACK -- mesmo que o
-- teste falhe, nada fica gravado. Os produtos reais nao sao afetados
-- porque o teste aponta o script para os nomes de mentira.
--
-- Uso (na pasta scripts):
--   psql -h localhost -U postgres -d imperial_colors -f testar_corrigir_fiscal_parana_color.sql
--
-- Cenario extra (regime nao-Simples deve BLOQUEAR; o psql sai com erro):
--   psql ... -v cenario=regime_normal -f testar_corrigir_fiscal_parana_color.sql
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off
\pset null '(vazio)'

BEGIN;

-- ---------------- Massa de teste ----------------
INSERT INTO categorias (nome, criado_em, ativo) VALUES
  ('ZZ TESTE TINTA',    localtimestamp, true),   -- regra 1 (CFOP 5403/6403, CSOSN 500)
  ('ZZ TESTE REVEST',   localtimestamp, true),   -- regra 2 (CFOP 5102/6102, CSOSN 102, PIS/COFINS 08)
  ('ZZ TESTE OUTRA',    localtimestamp, true);   -- fora de qualquer regra

INSERT INTO marcas (nome, criado_em, ativo) VALUES
  ('ZZ TESTE PARANA',             localtimestamp, true),   -- marca alvo
  ('zz teste paran' || U&'\00E1', localtimestamp, true),   -- mesma marca, minuscula e com acento
  ('ZZ TESTE PARANA 2 BD',        localtimestamp, true);   -- marca parecida, NAO e alvo

-- codigo -> situacao esperada
--  TINTA (regra 1)
--   R-1  alvo, estado do script anterior, mas com origem 1 (estrangeira)
--          -> CFOP 5403/6403, CSOSN 500 e origem 0; PIS/COFINS 08 PRESERVADOS
--   R-2  alvo, com CST ICMS 60, origem VAZIA e PIS/COFINS vazios
--          -> CSOSN 500, origem 0, CST ICMS zerado, PIS/COFINS continuam VAZIOS (regra 1 nao cita)
--   R-3  alvo, SEM linha fiscal            -> linha criada com origem, CFOP e CSOSN
--   R-4  alvo, ja corrigido                -> nao pode ser tocado
--   R-5  alvo, marca escrita com acento    -> corrigido (comparacao sem acento/caixa)
--  REVESTIMENTO (regra 2)
--   V-1  alvo, com valores de ST (5403/6403/500), origem 1 -> 5102/6102/102 + PIS/COFINS 08 e origem 0
--   V-2  alvo, SEM linha fiscal            -> linha criada com os 6 campos
--   V-3  alvo, PIS/COFINS 01               -> vira 08
--   V-4  alvo, ja corrigido                -> nao pode ser tocado
--  Fora do alvo (nao pode ser tocado)
--   X-1  TINTA inativo
--   X-2  TINTA da marca parecida (2 BD), origem 1 (tem que continuar 1)
--   X-3  outra categoria, marca alvo, origem 1 (tem que continuar 1)
INSERT INTO produtos (codigo_interno, nome, categoria_id, marca_id, quantidade_estoque,
                      estoque_minimo, unidade, preco_venda, criado_em, ativo)
SELECT v.codigo, 'ZZ ' || v.codigo, c.id, m.id, 1, 0, 'UN', 10, localtimestamp, v.ativo
FROM (VALUES
  ('ZZ-R-1', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA',             true),
  ('ZZ-R-2', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA',             true),
  ('ZZ-R-3', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA',             true),
  ('ZZ-R-4', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA',             true),
  ('ZZ-R-5', 'ZZ TESTE TINTA',  'zz teste paran' || U&'\00E1', true),
  ('ZZ-V-1', 'ZZ TESTE REVEST', 'ZZ TESTE PARANA',             true),
  ('ZZ-V-2', 'ZZ TESTE REVEST', 'ZZ TESTE PARANA',             true),
  ('ZZ-V-3', 'ZZ TESTE REVEST', 'ZZ TESTE PARANA',             true),
  ('ZZ-V-4', 'ZZ TESTE REVEST', 'ZZ TESTE PARANA',             true),
  ('ZZ-X-1', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA',             false),
  ('ZZ-X-2', 'ZZ TESTE TINTA',  'ZZ TESTE PARANA 2 BD',        true),
  ('ZZ-X-3', 'ZZ TESTE OUTRA',  'ZZ TESTE PARANA',             true)
) AS v(codigo, categoria, marca, ativo)
JOIN categorias c ON c.nome = v.categoria
JOIN marcas m ON m.nome = v.marca;

INSERT INTO tributacao_produtos
  (produto_id, ncm, origem, cst_icms, csosn_icms, aliquota_icms_st_retido,
   cfop_dentro_estado, cfop_fora_estado, cst_pis, cst_cofins, atualizado_em)
SELECT p.id, v.ncm, v.origem, v.cst_icms, v.csosn, v.pst, v.cfop_d, v.cfop_f, v.pis, v.cofins,
       TIMESTAMP '2020-01-01 00:00:00'
FROM (VALUES
  ('ZZ-R-1', '32091010', 1,    NULL, '102', NULL,  '5102', '6102', '08', '08'),
  ('ZZ-R-2', '32091010', NULL, '60', NULL,  NULL,  '5101', '6101', NULL, NULL),
  ('ZZ-R-4', '32091010', 0,    NULL, '500', 17.00, '5403', '6403', '08', '08'),
  ('ZZ-R-5', '32091010', NULL, NULL, NULL,  NULL,  NULL,   NULL,   NULL, NULL),
  ('ZZ-V-1', '32091010', 1,    NULL, '500', 17.00, '5403', '6403', '08', '08'),  -- origem 1 -> 0; pST 17 preservado
  ('ZZ-V-3', '32091010', 0,    NULL, '500', NULL,  '5101', '6101', '01', '01'),
  ('ZZ-V-4', '32091010', 0,    NULL, '102', NULL,  '5102', '6102', '08', '08'),
  ('ZZ-X-2', '32091010', 1,    NULL, '102', NULL,  '5102', '6102', '08', '08'),
  ('ZZ-X-3', '32091010', 1,    NULL, '102', NULL,  '5102', '6102', '08', '08')
) AS v(codigo, ncm, origem, cst_icms, csosn, pst, cfop_d, cfop_f, pis, cofins)
JOIN produtos p ON p.codigo_interno = v.codigo;

-- ---------------- Cenario extra: regime nao-Simples ----------------
\if :{?cenario}
  INSERT INTO parametros_sistema (chave, valor_texto, criado_em)
  VALUES ('RegimeTributarioEmpresa', 'LucroReal', localtimestamp);
  \echo '>>> cenario regime_normal: o script abaixo DEVE falhar com erro de regime <<<'
\endif

-- Retrato do estado original (para provar o "desfazer" no fim)
CREATE TEMP TABLE original AS
SELECT p.codigo_interno, t.csosn_icms, t.cst_icms, t.cfop_dentro_estado, t.cfop_fora_estado,
       t.cst_pis, t.cst_cofins, t.atualizado_em, t.ncm, t.origem, t.aliquota_icms_st_retido,
       (t.produto_id IS NOT NULL) AS tem_linha
FROM produtos p
LEFT JOIN tributacao_produtos t ON t.produto_id = p.id
WHERE p.codigo_interno LIKE 'ZZ-%';

-- ---------------- Executa o script de verdade ----------------
\set marca 'ZZ TESTE PARANA'
\set categoria1 'ZZ TESTE TINTA'
\set categoria2 'ZZ TESTE REVEST'
\set teste 1
\ir corrigir_fiscal_parana_color.sql

-- ---------------- Conferencias ----------------
\echo
\echo '=== CONFERENCIAS DO TESTE ==='

-- Auxiliar: devolve a linha fiscal de um produto de teste.
CREATE FUNCTION pg_temp.trib(cod text) RETURNS tributacao_produtos
LANGUAGE sql AS $$
  SELECT x.* FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = cod
$$;

-- R-1: CFOP/CSOSN novos; PIS, COFINS, origem e NCM preservados
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-R-1');
BEGIN
  IF (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms) IS DISTINCT FROM (0,'5403','6403','500') THEN
    RAISE EXCEPTION 'FALHA R-1: TINTA nao recebeu origem 0 e 5403/6403/500 (origem estrangeira 1 deveria virar 0)';
  END IF;
  IF (t.cst_pis, t.cst_cofins, t.ncm) IS DISTINCT FROM ('08','08','32091010') THEN
    RAISE EXCEPTION 'FALHA R-1: PIS/COFINS/NCM de TINTA foram alterados e a regra nao manda mexer';
  END IF;
  RAISE NOTICE 'OK  R-1  TINTA: origem 1 -> 0, CFOP 5403/6403 e CSOSN 500; PIS/COFINS/NCM preservados';
END $$;

-- R-2: CST ICMS zerado; PIS/COFINS vazios continuam vazios (TINTA nao define PIS/COFINS)
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-R-2');
BEGIN
  IF (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms) IS DISTINCT FROM (0,'5403','6403','500') THEN
    RAISE EXCEPTION 'FALHA R-2: TINTA com origem vazia nao recebeu origem 0 e 5403/6403/500';
  END IF;
  IF t.cst_icms IS NOT NULL THEN RAISE EXCEPTION 'FALHA R-2: CST do ICMS deveria ter sido zerado'; END IF;
  IF t.cst_pis IS NOT NULL OR t.cst_cofins IS NOT NULL THEN
    RAISE EXCEPTION 'FALHA R-2: PIS/COFINS foram preenchidos em TINTA e a regra nao manda';
  END IF;
  RAISE NOTICE 'OK  R-2  TINTA: origem vazia -> 0, CST ICMS zerado; PIS/COFINS vazios continuaram vazios';
END $$;

-- R-3: sem linha -> criada so com CFOP e CSOSN
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-R-3');
BEGIN
  IF t.produto_id IS NULL THEN RAISE EXCEPTION 'FALHA R-3: linha fiscal nao foi criada'; END IF;
  IF (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms) IS DISTINCT FROM (0,'5403','6403','500') THEN
    RAISE EXCEPTION 'FALHA R-3: valores errados na linha criada';
  END IF;
  IF t.cst_pis IS NOT NULL OR t.cst_cofins IS NOT NULL OR t.ncm IS NOT NULL THEN
    RAISE EXCEPTION 'FALHA R-3: linha criada tem campos preenchidos que a regra nao cita';
  END IF;
  RAISE NOTICE 'OK  R-3  TINTA sem linha ganhou a linha com origem 0, CFOP e CSOSN (sem PIS/COFINS/NCM)';
END $$;

-- R-4: ja corrigido -> intocado (inclusive o pST que nao faz parte da regra)
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-R-4');
BEGIN
  IF t.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA R-4: linha que ja estava correta foi regravada';
  END IF;
  IF t.aliquota_icms_st_retido IS DISTINCT FROM 17.00 THEN RAISE EXCEPTION 'FALHA R-4: pST foi alterado'; END IF;
  RAISE NOTICE 'OK  R-4  TINTA ja corrigido nao foi tocado';
END $$;

-- R-5: marca com acento/minuscula
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-R-5');
BEGIN
  IF (t.origem, t.cfop_dentro_estado, t.csosn_icms) IS DISTINCT FROM (0,'5403','500') THEN
    RAISE EXCEPTION 'FALHA R-5: marca com acento/minuscula nao foi encontrada';
  END IF;
  RAISE NOTICE 'OK  R-5  marca com acento e minuscula foi encontrada';
END $$;

-- V-1: ST -> 102; origem 1 preservada; pST preservado (regra nao cita)
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-V-1');
BEGIN
  IF (t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms, t.cst_pis, t.cst_cofins)
     IS DISTINCT FROM ('5102','6102','102','08','08') THEN
    RAISE EXCEPTION 'FALHA V-1: REVESTIMENTO nao recebeu 5102/6102/102/08/08';
  END IF;
  IF t.origem IS DISTINCT FROM 0 THEN RAISE EXCEPTION 'FALHA V-1: origem estrangeira 1 deveria ter virado 0 - Nacional'; END IF;
  IF t.aliquota_icms_st_retido IS DISTINCT FROM 17.00 THEN RAISE EXCEPTION 'FALHA V-1: pST foi alterado'; END IF;
  RAISE NOTICE 'OK  V-1  REVESTIMENTO: 5102/6102/102, PIS/COFINS 08 e origem 1 -> 0; pST preservado';
END $$;

-- V-2: sem linha -> criada com os 5 campos
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-V-2');
BEGIN
  IF t.produto_id IS NULL THEN RAISE EXCEPTION 'FALHA V-2: linha fiscal nao foi criada'; END IF;
  IF (t.origem, t.cfop_dentro_estado, t.cfop_fora_estado, t.csosn_icms, t.cst_pis, t.cst_cofins)
     IS DISTINCT FROM (0,'5102','6102','102','08','08') THEN
    RAISE EXCEPTION 'FALHA V-2: valores errados na linha criada';
  END IF;
  RAISE NOTICE 'OK  V-2  REVESTIMENTO sem linha ganhou a linha com os 6 campos';
END $$;

-- V-3: PIS/COFINS 01 -> 08
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-V-3');
BEGIN
  IF (t.csosn_icms, t.cst_pis, t.cst_cofins) IS DISTINCT FROM ('102','08','08') THEN
    RAISE EXCEPTION 'FALHA V-3: PIS/COFINS 01 nao viraram 08';
  END IF;
  RAISE NOTICE 'OK  V-3  REVESTIMENTO: PIS/COFINS 01 -> 08';
END $$;

-- V-4: ja corrigido -> intocado
DO $$
DECLARE t tributacao_produtos := pg_temp.trib('ZZ-V-4');
BEGIN
  IF t.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA V-4: linha que ja estava correta foi regravada';
  END IF;
  RAISE NOTICE 'OK  V-4  REVESTIMENTO ja corrigido nao foi tocado';
END $$;

-- X-1/2/3: fora do alvo, intocados (inativo, marca parecida, outra categoria)
DO $$
DECLARE x1 tributacao_produtos := pg_temp.trib('ZZ-X-1');
        x2 tributacao_produtos := pg_temp.trib('ZZ-X-2');
        x3 tributacao_produtos := pg_temp.trib('ZZ-X-3');
BEGIN
  IF x1.produto_id IS NOT NULL THEN RAISE EXCEPTION 'FALHA X-1: produto INATIVO recebeu tributacao'; END IF;
  IF (x2.origem, x2.csosn_icms, x2.cfop_dentro_estado) IS DISTINCT FROM (1,'102','5102') OR x2.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA X-2: marca parecida (2 BD) foi alterada e nao podia';
  END IF;
  IF (x3.origem, x3.csosn_icms, x3.cfop_dentro_estado) IS DISTINCT FROM (1,'102','5102') OR x3.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA X-3: outra categoria foi alterada e nao podia';
  END IF;
  RAISE NOTICE 'OK  X-1/2/3  inativo, marca parecida e outra categoria ficaram intactos';
END $$;

-- Regras nao se misturam: nenhuma TINTA ficou com 5102 e nenhum REVESTIMENTO com 5403
DO $$
DECLARE cruzados integer;
BEGIN
  SELECT count(*) INTO cruzados
  FROM produtos p
  JOIN categorias c ON c.id = p.categoria_id
  JOIN tributacao_produtos t ON t.produto_id = p.id
  WHERE p.codigo_interno LIKE 'ZZ-%' AND p.ativo
    AND ((c.nome = 'ZZ TESTE TINTA'  AND p.codigo_interno NOT IN ('ZZ-X-2') AND t.cfop_dentro_estado <> '5403')
      OR (c.nome = 'ZZ TESTE REVEST' AND t.cfop_dentro_estado <> '5102'));
  IF cruzados <> 0 THEN RAISE EXCEPTION 'FALHA: % produto(s) receberam a regra da outra categoria', cruzados; END IF;
  RAISE NOTICE 'OK  as duas regras nao se misturaram';
END $$;

-- Backup: 9 produtos alvo (R-1..R-5, V-1..V-4); sem linha = R-3 e V-2
SELECT set_config('teste.bkp', :'bkp', false);
DO $$
DECLARE
  nome_bkp text := current_setting('teste.bkp'); total integer; sem_linha integer;
BEGIN
  IF to_regclass(nome_bkp) IS NULL THEN RAISE EXCEPTION 'FALHA backup: tabela de copia nao foi criada'; END IF;
  EXECUTE format('SELECT count(*), count(*) FILTER (WHERE NOT tinha_linha) FROM %I', nome_bkp) INTO total, sem_linha;
  IF total <> 9 OR sem_linha <> 2 THEN
    RAISE EXCEPTION 'FALHA backup: esperado 9 produtos (2 sem linha), veio % (% sem linha)', total, sem_linha;
  END IF;
  RAISE NOTICE 'OK  backup  % guarda os 9 produtos alvo e marca os 2 que nao tinham linha', nome_bkp;
END $$;

-- Segunda execucao: nao altera nada e nao cria copia nova
\echo
\echo '=== SEGUNDA EXECUCAO (idempotencia) ==='
SELECT max(x.atualizado_em) AS antes_da_2a_execucao
FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id
WHERE p.codigo_interno IN ('ZZ-R-1','ZZ-R-2','ZZ-R-3','ZZ-R-5','ZZ-V-1','ZZ-V-2','ZZ-V-3') \gset
SELECT set_config('teste.antes', :'antes_da_2a_execucao', false);
SELECT count(*) AS copias_antes FROM pg_tables WHERE tablename LIKE 'bkp_trib_corrigir_parana_color_%' \gset
SELECT pg_sleep(1.1);
\ir corrigir_fiscal_parana_color.sql
SELECT count(*) AS copias_depois FROM pg_tables WHERE tablename LIKE 'bkp_trib_corrigir_parana_color_%' \gset
SELECT (:copias_antes = :copias_depois) AS nao_criou_copia_nova \gset
\if :nao_criou_copia_nova
  \echo OK  2a execucao nao criou copia de seguranca nova
\else
  \echo FALHA: 2a execucao criou uma copia nova (deveria pular, nada a alterar)
  SELECT 1/0;
\endif

DO $$
DECLARE mudadas integer;
BEGIN
  SELECT count(*) INTO mudadas
  FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id
  WHERE p.codigo_interno LIKE 'ZZ-%'
    AND x.atualizado_em > current_setting('teste.antes')::timestamp;
  IF mudadas <> 0 THEN RAISE EXCEPTION 'FALHA idempotencia: segunda execucao alterou % linha(s)', mudadas; END IF;
  RAISE NOTICE 'OK  segunda execucao nao alterou nada';
END $$;

-- Desfazer: usa a copia da 1a execucao (variavel :bkp) e tem que voltar ao original
\echo
\echo === DESFAZER ===
\ir desfazer_fiscal_parana_color.sql

DO $$
DECLARE diferentes integer;
BEGIN
  SELECT count(*) INTO diferentes FROM (
    SELECT p.codigo_interno, t.csosn_icms, t.cst_icms, t.cfop_dentro_estado, t.cfop_fora_estado,
           t.cst_pis, t.cst_cofins, t.atualizado_em, t.ncm, t.origem, t.aliquota_icms_st_retido,
           (t.produto_id IS NOT NULL) AS tem_linha
    FROM produtos p
    LEFT JOIN tributacao_produtos t ON t.produto_id = p.id
    WHERE p.codigo_interno LIKE 'ZZ-%'
    EXCEPT
    SELECT * FROM original
  ) x;
  IF diferentes <> 0 THEN
    RAISE EXCEPTION 'FALHA desfazer: % produto(s) nao voltaram ao estado original', diferentes;
  END IF;
  RAISE NOTICE 'OK  desfazer devolveu todos os produtos ao estado original (inclusive apagou as linhas criadas)';
END $$;

\echo
\echo '*** TODOS OS TESTES PASSARAM -- desfazendo a massa de teste (ROLLBACK) ***'
ROLLBACK;

-- Prova de que nada ficou no banco.
SELECT (SELECT count(*) FROM produtos   WHERE codigo_interno LIKE 'ZZ-%')       AS produtos_de_teste_restantes,
       (SELECT count(*) FROM categorias WHERE nome LIKE 'ZZ TESTE%')              AS categorias_de_teste_restantes,
       (SELECT count(*) FROM marcas     WHERE nome ILIKE 'ZZ TESTE%')             AS marcas_de_teste_restantes,
       (SELECT count(*) FROM pg_tables  WHERE tablename LIKE 'bkp_trib_corrigir_parana_color_%') AS tabelas_bkp_restantes;
