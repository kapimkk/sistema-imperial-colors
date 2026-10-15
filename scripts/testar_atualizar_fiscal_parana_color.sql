-- =====================================================================
-- testar_atualizar_fiscal_parana_color.sql
--
-- Teste automatizado do atualizar_fiscal_parana_color.sql.
--
-- NAO ALTERA O BANCO: cria categoria, marcas e produtos de mentira com
-- nomes "ZZ TESTE ...", roda o script de verdade sobre eles e confere o
-- resultado. Tudo dentro de uma transacao que termina em ROLLBACK -- mesmo
-- que o teste falhe, nada fica gravado. Os produtos reais nao sao
-- afetados porque o teste aponta o script para os nomes de mentira.
--
-- Uso (na pasta scripts):
--   psql -h localhost -U postgres -d imperial_colors -f testar_atualizar_fiscal_parana_color.sql
--
-- Cenario extra (regime tributario nao-Simples deve BLOQUEAR o script;
-- o psql sai com erro e o resultado esperado e a falha):
--   psql ... -v cenario=regime_normal -f testar_atualizar_fiscal_parana_color.sql
-- =====================================================================

\set ON_ERROR_STOP on
\pset pager off
\pset null '(vazio)'

BEGIN;

-- ---------------- Massa de teste ----------------
INSERT INTO categorias (nome, criado_em, ativo) VALUES
  ('ZZ TESTE TINTA',  localtimestamp, true),
  ('ZZ TESTE OUTRA',  localtimestamp, true);

INSERT INTO marcas (nome, criado_em, ativo) VALUES
  ('ZZ TESTE PARANA',          localtimestamp, true),   -- marca alvo
  ('zz teste paran' || U&'\00E1', localtimestamp, true), -- mesma marca, minuscula e com acento
  ('ZZ TESTE PARANA 2 BD',     localtimestamp, true),   -- marca parecida, NAO e alvo
  ('ZZ TESTE ATLAS',           localtimestamp, true);

-- codigo_interno -> produto
--   T-A  alvo, SEM linha fiscal            -> linha deve ser criada
--   T-B  alvo, com NCM/CST/CFOP antigos    -> 5 campos mudam, NCM e resto ficam
--   T-C  alvo, ja com os valores corretos  -> nao pode ser tocado
--   T-D  alvo mas INATIVO                  -> nao pode ser tocado
--   T-E  marca parecida (2 BD)             -> nao pode ser tocado
--   T-F  outra categoria                   -> nao pode ser tocado
--   T-G  marca alvo escrita com acento     -> deve ser atualizado (comparacao sem acento/caixa)
--   T-H  alvo, linha existente com tudo vazio -> origem vira 0 e os demais campos tambem
INSERT INTO produtos (codigo_interno, nome, categoria_id, marca_id, quantidade_estoque,
                      estoque_minimo, unidade, preco_venda, criado_em, ativo)
SELECT v.codigo, 'ZZ ' || v.codigo, c.id, m.id, 1, 0, 'UN', 10, localtimestamp, v.ativo
FROM (VALUES
  ('ZZ-T-A', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA',          true),
  ('ZZ-T-B', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA',          true),
  ('ZZ-T-C', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA',          true),
  ('ZZ-T-D', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA',          false),
  ('ZZ-T-E', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA 2 BD',     true),
  ('ZZ-T-F', 'ZZ TESTE OUTRA', 'ZZ TESTE PARANA',          true),
  ('ZZ-T-G', 'ZZ TESTE TINTA', 'zz teste paran' || U&'\00E1', true),
  ('ZZ-T-H', 'ZZ TESTE TINTA', 'ZZ TESTE PARANA',          true)
) AS v(codigo, categoria, marca, ativo)
JOIN categorias c ON c.nome = v.categoria
JOIN marcas m ON m.nome = v.marca;

INSERT INTO tributacao_produtos
  (produto_id, ncm, origem, cst_icms, csosn_icms, aliquota_icms,
   cfop_dentro_estado, cfop_fora_estado, cst_pis, cst_cofins, atualizado_em)
SELECT p.id, v.ncm, v.origem, v.cst_icms, v.csosn, v.aliq, v.cfop_d, v.cfop_f, v.pis, v.cofins, v.quando
FROM (VALUES
  -- T-B: dados antigos e diferentes (origem 1 = estrangeira, que deve virar 0), com NCM que NAO pode se perder
  ('ZZ-T-B', '32091010', 1, '00', NULL,  18.00, '5101', '6101', '01', '01', TIMESTAMP '2020-01-01 00:00:00'),
  -- T-C: ja esta exatamente no padrao (origem 0 inclusive); atualizado_em antigo tem que continuar igual
  ('ZZ-T-C', '32091010', 0, NULL, '102', NULL,  '5102', '6102', '08', '08', TIMESTAMP '2020-01-01 00:00:00'),
  -- T-E e T-F: valores antigos (origem 1) que precisam sobreviver intactos
  ('ZZ-T-E', '32091010', 1, NULL, '500', NULL,  '5101', '6101', '01', '01', TIMESTAMP '2020-01-01 00:00:00'),
  ('ZZ-T-F', '32091010', 1, NULL, '500', NULL,  '5101', '6101', '01', '01', TIMESTAMP '2020-01-01 00:00:00'),
  -- T-H: linha ja existente com origem VAZIA (o caso comum) -> deve virar 0
  ('ZZ-T-H', '32091010', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, TIMESTAMP '2020-01-01 00:00:00')
) AS v(codigo, ncm, origem, cst_icms, csosn, aliq, cfop_d, cfop_f, pis, cofins, quando)
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
       t.cst_pis, t.cst_cofins, t.atualizado_em, t.ncm, t.origem, t.aliquota_icms,
       (t.produto_id IS NOT NULL) AS tem_linha
FROM produtos p
LEFT JOIN tributacao_produtos t ON t.produto_id = p.id
WHERE p.codigo_interno LIKE 'ZZ-T-%';

-- ---------------- Executa o script de verdade ----------------
\set categoria 'ZZ TESTE TINTA'
\set marca 'ZZ TESTE PARANA'
\set teste 1
\ir atualizar_fiscal_parana_color.sql

-- ---------------- Conferencias ----------------
\echo
\echo '=== CONFERENCIAS DO TESTE ==='

-- 1) T-A ganhou linha com os 5 valores
DO $$
DECLARE t tributacao_produtos%ROWTYPE;
BEGIN
  SELECT x.* INTO t FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-A';
  IF t.produto_id IS NULL THEN RAISE EXCEPTION 'FALHA T-A: linha fiscal nao foi criada'; END IF;
  IF (t.origem, t.csosn_icms, t.cfop_dentro_estado, t.cfop_fora_estado, t.cst_pis, t.cst_cofins) IS DISTINCT FROM (0,'102','5102','6102','08','08') THEN
    RAISE EXCEPTION 'FALHA T-A: valores errados';
  END IF;
  IF t.ncm IS NOT NULL THEN RAISE EXCEPTION 'FALHA T-A: NCM deveria continuar vazio'; END IF;
  RAISE NOTICE 'OK  T-A  produto sem linha fiscal ganhou a linha com os 6 valores (origem 0 inclusive)';
END $$;

-- 2) T-B trocou os 5, zerou o CST do ICMS e PRESERVOU NCM/origem/aliquota
DO $$
DECLARE t tributacao_produtos%ROWTYPE;
BEGIN
  SELECT x.* INTO t FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-B';
  IF (t.origem, t.csosn_icms, t.cfop_dentro_estado, t.cfop_fora_estado, t.cst_pis, t.cst_cofins) IS DISTINCT FROM (0,'102','5102','6102','08','08') THEN
    RAISE EXCEPTION 'FALHA T-B: valores nao foram atualizados (origem estrangeira 1 deveria virar 0)';
  END IF;
  IF t.cst_icms IS NOT NULL THEN RAISE EXCEPTION 'FALHA T-B: CST do ICMS deveria ter sido zerado (excludente com CSOSN)'; END IF;
  IF t.ncm IS DISTINCT FROM '32091010' OR t.aliquota_icms IS DISTINCT FROM 18.00 THEN
    RAISE EXCEPTION 'FALHA T-B: NCM/aliquota foram alterados e nao podiam';
  END IF;
  IF t.atualizado_em < TIMESTAMP '2026-01-01' THEN RAISE EXCEPTION 'FALHA T-B: atualizado_em nao foi atualizado'; END IF;
  RAISE NOTICE 'OK  T-B  atualizou os 6 campos (origem 1 -> 0), zerou CST do ICMS e preservou NCM/aliquota';
END $$;

-- 3) T-C ja estava correto: atualizado_em antigo tem que ter sido mantido
DO $$
DECLARE quando timestamp;
BEGIN
  SELECT x.atualizado_em INTO quando FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-C';
  IF quando IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA T-C: linha que ja estava correta foi regravada (atualizado_em mudou)';
  END IF;
  RAISE NOTICE 'OK  T-C  linha que ja estava correta nao foi tocada';
END $$;

-- 4) T-D (inativo), T-E (marca parecida), T-F (outra categoria): intocados
DO $$
DECLARE
  d integer; e tributacao_produtos%ROWTYPE; f tributacao_produtos%ROWTYPE;
BEGIN
  SELECT count(*) INTO d FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-D';
  IF d <> 0 THEN RAISE EXCEPTION 'FALHA T-D: produto INATIVO recebeu tributacao'; END IF;

  SELECT x.* INTO e FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-E';
  IF (e.origem, e.csosn_icms, e.cfop_dentro_estado, e.cst_pis) IS DISTINCT FROM (1,'500','5101','01')
     OR e.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA T-E: marca parecida (2 BD) foi alterada e nao podia';
  END IF;

  SELECT x.* INTO f FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-F';
  IF (f.origem, f.csosn_icms, f.cfop_dentro_estado, f.cst_pis) IS DISTINCT FROM (1,'500','5101','01')
     OR f.atualizado_em IS DISTINCT FROM TIMESTAMP '2020-01-01 00:00:00' THEN
    RAISE EXCEPTION 'FALHA T-F: produto de outra categoria foi alterado e nao podia';
  END IF;
  RAISE NOTICE 'OK  T-D/E/F  inativo, marca parecida e outra categoria ficaram intactos';
END $$;

-- 5) T-G: marca com acento/minuscula casa pela comparacao normalizada
DO $$
DECLARE t tributacao_produtos%ROWTYPE;
BEGIN
  SELECT x.* INTO t FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-G';
  IF t.csosn_icms IS DISTINCT FROM '102' THEN RAISE EXCEPTION 'FALHA T-G: marca com acento/minuscula nao foi encontrada'; END IF;
  RAISE NOTICE 'OK  T-G  marca com acento e minuscula foi encontrada';
END $$;

-- 5b) T-H: linha existente com tudo vazio recebe os 6 valores
DO $$
DECLARE t tributacao_produtos%ROWTYPE;
BEGIN
  SELECT x.* INTO t FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id WHERE p.codigo_interno = 'ZZ-T-H';
  IF (t.origem, t.csosn_icms, t.cfop_dentro_estado, t.cfop_fora_estado, t.cst_pis, t.cst_cofins) IS DISTINCT FROM (0,'102','5102','6102','08','08') THEN
    RAISE EXCEPTION 'FALHA T-H: linha com origem vazia nao recebeu 0 - Nacional';
  END IF;
  IF t.ncm IS DISTINCT FROM '32091010' THEN RAISE EXCEPTION 'FALHA T-H: NCM foi alterado'; END IF;
  RAISE NOTICE 'OK  T-H  linha com origem vazia passou a 0 - Nacional';
END $$;

-- 6) Backup: tem os 5 produtos alvo e marca quem nao tinha linha
-- (o nome da tabela vem da variavel :bkp que o script principal definiu)
SELECT set_config('teste.bkp', :'bkp', false);
DO $$
DECLARE
  nome_bkp text := current_setting('teste.bkp'); total integer; sem_linha integer;
BEGIN
  IF to_regclass(nome_bkp) IS NULL THEN RAISE EXCEPTION 'FALHA backup: tabela de copia de seguranca nao foi criada'; END IF;

  EXECUTE format('SELECT count(*), count(*) FILTER (WHERE NOT tinha_linha) FROM %I', nome_bkp)
    INTO total, sem_linha;
  -- alvo = A, B, C, G, H (D inativo, E/F fora); sem linha = A e G
  IF total <> 5 OR sem_linha <> 2 THEN
    RAISE EXCEPTION 'FALHA backup: esperado 5 produtos (2 sem linha), veio % (% sem linha)', total, sem_linha;
  END IF;
  RAISE NOTICE 'OK  backup  % guarda os 5 produtos alvo e marca os 2 que nao tinham linha', nome_bkp;
END $$;

-- 7) Segunda execucao: nao pode alterar mais nada
\echo
\echo '=== SEGUNDA EXECUCAO (idempotencia) ==='
\set categoria 'ZZ TESTE TINTA'
\set marca 'ZZ TESTE PARANA'
SELECT max(x.atualizado_em) AS antes_da_2a_execucao
FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id
WHERE p.codigo_interno IN ('ZZ-T-A','ZZ-T-B','ZZ-T-G','ZZ-T-H') \gset
SELECT set_config('teste.antes', :'antes_da_2a_execucao', false);

-- Se criasse outra copia, ela guardaria o estado JA corrigido e enganaria o desfazer.
SELECT count(*) AS copias_antes FROM pg_tables WHERE tablename LIKE 'bkp_trib_parana_color_%' \gset
SELECT pg_sleep(1.1);
\ir atualizar_fiscal_parana_color.sql
SELECT count(*) AS copias_depois FROM pg_tables WHERE tablename LIKE 'bkp_trib_parana_color_%' \gset
SELECT (:copias_antes = :copias_depois) AS nao_criou_copia_nova \gset
\if :nao_criou_copia_nova
  \echo OK  2a execucao nao criou copia de seguranca nova
\else
  \echo FALHA: 2a execucao criou uma copia nova (deveria pular, nada a alterar)
  SELECT 1/0;
\endif

DO $$
DECLARE linhas_mudadas integer;
BEGIN
  SELECT count(*) INTO linhas_mudadas
  FROM tributacao_produtos x JOIN produtos p ON p.id = x.produto_id
  WHERE p.codigo_interno IN ('ZZ-T-A','ZZ-T-B','ZZ-T-G','ZZ-T-H')
    AND x.atualizado_em > current_setting('teste.antes')::timestamp;
  IF linhas_mudadas <> 0 THEN
    RAISE EXCEPTION 'FALHA idempotencia: segunda execucao alterou % linha(s)', linhas_mudadas;
  END IF;
  RAISE NOTICE 'OK  segunda execucao nao alterou nada';
END $$;

-- 8) Desfazer: usa a copia da 1a execucao (variavel :bkp) e tem que voltar ao original
\echo
\echo === DESFAZER ===
\ir desfazer_fiscal_parana_color.sql

DO $$
DECLARE diferentes integer;
BEGIN
  SELECT count(*) INTO diferentes FROM (
    SELECT p.codigo_interno, t.csosn_icms, t.cst_icms, t.cfop_dentro_estado, t.cfop_fora_estado,
           t.cst_pis, t.cst_cofins, t.atualizado_em, t.ncm, t.origem, t.aliquota_icms,
           (t.produto_id IS NOT NULL) AS tem_linha
    FROM produtos p
    LEFT JOIN tributacao_produtos t ON t.produto_id = p.id
    WHERE p.codigo_interno LIKE 'ZZ-T-%'
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
SELECT (SELECT count(*) FROM produtos   WHERE codigo_interno LIKE 'ZZ-T-%')  AS produtos_de_teste_restantes,
       (SELECT count(*) FROM categorias WHERE nome LIKE 'ZZ TESTE%')           AS categorias_de_teste_restantes,
       (SELECT count(*) FROM marcas     WHERE nome ILIKE 'ZZ TESTE%')          AS marcas_de_teste_restantes,
       (SELECT count(*) FROM pg_tables  WHERE tablename LIKE 'bkp_trib_parana_color_%') AS tabelas_bkp_restantes;
