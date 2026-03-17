# Referência Nginx — Servidor Automais.IO

Documentação de referência da configuração Nginx em produção no servidor **automais**.

---

## 1. Visão geral

| Item | Valor |
|------|--------|
| Servidor | `automais` (root@automais) |
| Sites configurados | `automais-api`, `automais.io` |
| Diretório de configs | `/etc/nginx/sites-available/` |
| Certificados SSL | Let's Encrypt (Certbot) |

### Arquivos no servidor

```bash
# Listar sites disponíveis
ls /etc/nginx/sites-available/
# automais-api   automais.io
```

Os arquivos em `sites-available` precisam estar linkados em `sites-enabled` para serem ativos:

```bash
ls -la /etc/nginx/sites-enabled/
# automais-api -> /etc/nginx/sites-available/automais-api
# automais.io  -> /etc/nginx/sites-available/automais.io
```

---

## 2. Site: automais-api (api.automais.io)

**Função:** Subdomínio da API. O front chama `https://api.automais.io/api/...` e o Nginx faz proxy para o backend.

### Resumo

| Parâmetro | Valor |
|-----------|--------|
| **Server name** | `api.automais.io` |
| **Backend** | `localhost:5001` (HTTPS) |
| **Upstream** | `backend_api`, keepalive 32, max_fails=3, fail_timeout=30s |
| **SSL** | Certbot — `/etc/letsencrypt/live/api.automais.io/` |
| **HTTP → HTTPS** | 301 para `https://api.automais.io` |

### Logs

- Access: `/var/log/nginx/automais-api-access.log`
- Error: `/var/log/nginx/automais-api-error.log`

### Limites e segurança

- `client_max_body_size 20M`
- Headers: `X-Frame-Options SAMEORIGIN`, `X-Content-Type-Options nosniff`, `Referrer-Policy strict-origin-when-cross-origin`

### Locations

| Location | Descrição | Backend | Observações |
|----------|-----------|---------|-------------|
| `/health` | Health check | `https://backend_api` | timeouts 5s, `access_log off` |
| `/api/hubs` | SignalR (WebSocket) | `https://backend_api` | rewrite `/api/hubs` → `/hubs`, upgrade WebSocket, timeouts 300s |
| `/api` | API REST | `https://backend_api` | connect 30s, send/read 120s |
| `/swagger` | Swagger UI | `https://backend_api` | opção de restringir por IP (comentado) |
| `/` | Qualquer outra rota | — | `return 404` |

### Backend

- Todas as rotas de proxy usam **HTTPS** para o backend (`proxy_pass https://backend_api`, `proxy_ssl_verify off`).
- Backend escuta em **porta 5001**.

### Comandos úteis

```bash
# Testar config
sudo nginx -t

# Recarregar Nginx
sudo systemctl reload nginx

# Renovar certificado (Certbot)
sudo certbot renew --nginx -d api.automais.io
```

---

## 3. Site: automais.io (frontend)

**Função:** Aplicação React (SPA) em `automais.io` e `www.automais.io`. Servir estáticos e fazer proxy de `/api` para o backend.

### Resumo

| Parâmetro | Valor |
|-----------|--------|
| **Server name** | `automais.io`, `www.automais.io` |
| **Root** | `/var/www/automais.io` |
| **Backend /api** | `http://localhost:5000` |
| **SSL** | Certbot — `/etc/letsencrypt/live/automais.io/` |
| **HTTP → HTTPS** | 301 para `https://automais.io` / `https://www.automais.io` |

### Logs

- Access: `/var/log/nginx/automais-front-access.log`
- Error: `/var/log/nginx/automais-front-error.log`

### Gzip

- Habilitado para: `text/plain`, `text/css`, `text/xml`, `text/javascript`, `application/x-javascript`, `application/javascript`, `application/xml+rss`, `application/json`
- `gzip_min_length 1024`, `gzip_vary on`

### Locations

| Location | Descrição | Comportamento |
|----------|-----------|----------------|
| `/api` | Proxy para API | `proxy_pass http://localhost:5000`, WebSocket support, timeouts 60s |
| `= /index.html` | Index da SPA | `Cache-Control: no-cache, no-store, must-revalidate` |
| `~* \.(jpg|jpeg|png|gif|ico|css|js|svg|woff|woff2|ttf|eot)$` | Assets estáticos | Cache 1 ano, `Cache-Control: public, immutable` |
| `/` | Rotas da SPA | `try_files $uri $uri/ @fallback`, sem cache |
| `@fallback` | Fallback React Router | `rewrite ^.*$ /index.html last` |

### Cache

- **index.html e rotas:** sem cache (sempre versão nova).
- **Assets com hash (ex.: Vite):** cache longo (1 ano), `immutable`.

### Comandos úteis

```bash
# Testar config
sudo nginx -t

# Recarregar Nginx
sudo systemctl reload nginx

# Renovar certificado
sudo certbot renew --nginx -d automais.io -d www.automais.io
```

---

## 4. Fluxo de requisições

```
                    ┌─────────────────────────────────────────┐
                    │            api.automais.io               │
                    │         (site: automais-api)             │
                    └─────────────────┬───────────────────────┘
                                      │
                    /health, /api/*, /api/hubs, /swagger
                                      │
                                      ▼
                    ┌─────────────────────────────────────────┐
                    │  Backend API (HTTPS) — localhost:5001    │
                    └─────────────────────────────────────────┘

                    ┌─────────────────────────────────────────┐
                    │     automais.io / www.automais.io       │
                    │        (site: automais.io)               │
                    └─────────────────┬───────────────────────┘
                                      │
              /api/* ─────────────────┼──────────────────────► localhost:5000
              /, /login, etc. ────────┼──────────────────────► /var/www/automais.io (SPA)
```

- **Front (automais.io):** usuário acessa o site e chama a API em **api.automais.io** (subdomínio), não em `automais.io/api` para a API principal. O proxy `location /api` no site automais.io pode ser usado em desenvolvimento ou para algum uso interno.
- **API pública:** em produção a API é servida em **api.automais.io** (config **automais-api**), backend em **5001** com HTTPS.

---

## 5. Deploy via GitHub Actions

O workflow **Deploy to Production** (`.github/workflows/deploy.yml`) do repositório **Automais.IO.server**:

- **Copia** para o servidor os arquivos `deploy/nginx-api.automais.io.conf` e `deploy/nginx-automais.io.conf` (em cada push na `main` ou via workflow_dispatch).
- **Cria** os diretórios `/etc/nginx/sites-available` e `/etc/nginx/sites-enabled` se não existirem (`mkdir -p`).
- **Substitui** as configs do Nginx no servidor:
  - `nginx-api.automais.io.conf` → `/etc/nginx/sites-available/automais-api`
  - `nginx-automais.io.conf` → `/etc/nginx/sites-available/automais.io`
- **Cria/atualiza** os symlinks em `sites-enabled` para `automais-api` e `automais.io`.
- **Testa** a configuração (`nginx -t`) e **recarrega** o Nginx (`systemctl reload nginx`) se o teste passar.

Ou seja: o deploy pelo Git já configura e substitui o Nginx e cria as pastas necessárias. Ajustes manuais (por exemplo SSL com Certbot) continuam sendo feitos no servidor ou em um script de primeira instalação.

---

## 6. Checklist de deploy / manutenção (manual)

1. **Copiar config do repositório (se não usar o deploy via Git):**
   - `automais-api` → `/etc/nginx/sites-available/automais-api`
   - `automais.io` → `/etc/nginx/sites-available/automais.io`

2. **Garantir symlinks em sites-enabled:**
   ```bash
   sudo ln -sf /etc/nginx/sites-available/automais-api /etc/nginx/sites-enabled/
   sudo ln -sf /etc/nginx/sites-available/automais.io /etc/nginx/sites-enabled/
   ```

3. **Testar e recarregar:**
   ```bash
   sudo nginx -t && sudo systemctl reload nginx
   ```

4. **SSL (primeira vez):**
   ```bash
   sudo certbot --nginx -d api.automais.io
   sudo certbot --nginx -d automais.io -d www.automais.io
   ```

5. **Renovação SSL (cron já deve existir):**
   ```bash
   sudo certbot renew --nginx
   ```

---

## 7. Diferenças entre repositório e servidor (referência)

| Aspecto | Repo (deploy/*.conf) | Servidor (estado atual) |
|---------|----------------------|--------------------------|
| API backend porta | 5000 (em alguns .conf) | **5001** (automais-api) |
| API backend protocolo | HTTP | **HTTPS** (proxy_ssl_verify off) |
| Frontend /api | localhost:5000 | localhost:5000 |
| API CORS | map $http_origin no repo | Não presente no config atual do servidor |
| API location / | proxy_pass backend | **return 404** |
| automais.io SignalR | /hubs no repo | No servidor atual o SignalR está em **api.automais.io** (/api/hubs) |

Ao atualizar os arquivos em `deploy/` para refletir produção, considerar: porta **5001**, backend em **HTTPS** e `return 404` em `location /` no automais-api.

---

*Documento gerado com base nos arquivos em `/etc/nginx/sites-available/` do servidor automais.*
