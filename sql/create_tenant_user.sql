-- ============================================================
-- Function: create_tenant_user
-- Descrição: Cria um novo usuário para um tenant no Automais.IO
-- 
-- Uso:
--   SELECT create_tenant_user(
--     'ID_DO_TENANT',           -- tenant_id (uuid)
--     'Nome do Usuário',        -- nome
--     'email@exemplo.com',      -- email
--     'SenhaDesejada',          -- senha (será hasheada com SHA256)
--     'Admin'                   -- role: Owner, Admin, Operator, Viewer
--   );
--
-- Exemplo real:
--   SELECT create_tenant_user(
--     'a1b2c3d4-e5f6-7890-abcd-ef1234567890',
--     'Bernardo Almeida',
--     'bernardo.almeida@automais.com',
--     'Admin123',
--     'Owner'
--   );
-- ============================================================

-- Garantir que a extensão pgcrypto está habilitada (necessária para digest/sha256)
CREATE EXTENSION IF NOT EXISTS pgcrypto;

CREATE OR REPLACE FUNCTION create_tenant_user(
    p_tenant_id UUID,
    p_name VARCHAR(120),
    p_email VARCHAR(150),
    p_password VARCHAR(255),
    p_role VARCHAR(20) DEFAULT 'Viewer'
)
RETURNS TABLE (
    user_id UUID,
    user_name VARCHAR,
    user_email VARCHAR,
    user_role VARCHAR,
    user_status VARCHAR,
    created_at TIMESTAMPTZ
)
LANGUAGE plpgsql
AS $$
DECLARE
    v_id UUID;
    v_password_hash TEXT;
    v_now TIMESTAMPTZ;
BEGIN
    -- Validações
    IF p_tenant_id IS NULL THEN
        RAISE EXCEPTION 'tenant_id é obrigatório';
    END IF;

    IF p_name IS NULL OR TRIM(p_name) = '' THEN
        RAISE EXCEPTION 'nome é obrigatório';
    END IF;

    IF p_email IS NULL OR TRIM(p_email) = '' THEN
        RAISE EXCEPTION 'email é obrigatório';
    END IF;

    IF p_password IS NULL OR TRIM(p_password) = '' THEN
        RAISE EXCEPTION 'senha é obrigatória';
    END IF;

    -- Validar role
    IF p_role NOT IN ('Owner', 'Admin', 'Operator', 'Viewer') THEN
        RAISE EXCEPTION 'role inválida. Valores aceitos: Owner, Admin, Operator, Viewer';
    END IF;

    -- Verificar se o tenant existe (colunas PascalCase precisam de aspas duplas)
    IF NOT EXISTS (SELECT 1 FROM public.tenants WHERE "Id" = p_tenant_id) THEN
        RAISE EXCEPTION 'Tenant com ID % não encontrado', p_tenant_id;
    END IF;

    -- Verificar se o email já existe no tenant
    IF EXISTS (SELECT 1 FROM public.tenant_users WHERE "TenantId" = p_tenant_id AND "Email" = p_email) THEN
        RAISE EXCEPTION 'Já existe um usuário com o email % neste tenant', p_email;
    END IF;

    -- Gerar ID e timestamp
    v_id := gen_random_uuid();
    v_now := NOW() AT TIME ZONE 'UTC';

    -- Gerar hash SHA256 da senha (mesmo algoritmo usado pelo C#)
    -- encode(digest('senha', 'sha256'), 'base64') produz o mesmo resultado que
    -- Convert.ToBase64String(SHA256.ComputeHash(Encoding.UTF8.GetBytes("senha")))
    v_password_hash := encode(digest(convert_to(p_password, 'UTF8'), 'sha256'), 'base64');

    -- Inserir o usuário
    -- Nota: EF Core sem UseSnakeCaseNamingConvention usa PascalCase para colunas
    INSERT INTO public.tenant_users (
        "Id",
        "TenantId",
        "Name",
        "Email",
        "PasswordHash",
        "Role",
        "Status",
        "VpnEnabled",
        "CreatedAt",
        "UpdatedAt"
    ) VALUES (
        v_id,
        p_tenant_id,
        p_name,
        p_email,
        v_password_hash,
        p_role,
        'Active',
        false,
        v_now,
        v_now
    );

    -- Retornar dados do usuário criado
    RETURN QUERY
    SELECT 
        v_id,
        p_name::VARCHAR,
        p_email::VARCHAR,
        p_role::VARCHAR,
        'Active'::VARCHAR,
        v_now;
END;
$$;

-- ============================================================
-- GRANT: dar permissão de execução (ajuste o role conforme necessário)
-- ============================================================
-- GRANT EXECUTE ON FUNCTION create_tenant_user TO seu_usuario;

-- ============================================================
-- Exemplos de uso:
-- ============================================================

-- Listar tenants disponíveis:
-- SELECT "Id", "Name" FROM public.tenants;

-- Criar um usuário Owner:
-- SELECT * FROM create_tenant_user(
--     'SEU-TENANT-ID-AQUI',
--     'Bernardo Almeida',
--     'bernardo.almeida@automais.com',
--     'Admin123',
--     'Owner'
-- );

-- Verificar se foi criado:
-- SELECT "Id", "Name", "Email", "Role", "Status" FROM public.tenant_users WHERE "Email" = 'bernardo.almeida@automais.com';
