-- Falha de entrega de e-mail (boas-vindas / reset) — espelha a migração EF TenantUserEmailDeliveryFailure
ALTER TABLE public.tenant_users
    ADD COLUMN IF NOT EXISTS "EmailDeliveryFailedAt" TIMESTAMPTZ NULL;

ALTER TABLE public.tenant_users
    ADD COLUMN IF NOT EXISTS "EmailDeliveryFailureMessage" VARCHAR(2000) NULL;
