# Deploy do subdomínio api.automais.io

O front em produção chama a API em **https://api.automais.io/api** (ex.: `POST /api/auth/login`).  
Se essa URL retornar **404**, o nginx no servidor provavelmente não está configurado para o host `api.automais.io`.

## 1. Conferir no servidor

No servidor onde a API .NET roda (porta 5000):

```bash
# A API está respondendo localmente?
curl -s -o /dev/null -w "%{http_code}" http://localhost:5000/api/auth/login -X POST -H "Content-Type: application/json" -d '{"username":"x","password":"y"}'
# Esperado: 401 (Unauthorized) — significa que a rota existe; 404 = rota não existe
```

Se retornar **401**, a aplicação está ok e o problema é só o proxy (nginx) para `api.automais.io`.

## 2. Configurar nginx para api.automais.io

1. Copiar o arquivo de configuração:
   ```bash
   sudo cp nginx-api.automais.io.conf /etc/nginx/sites-available/api.automais.io
   ```

2. Ativar o site:
   ```bash
   sudo ln -sf /etc/nginx/sites-available/api.automais.io /etc/nginx/sites-enabled/
   ```

3. Testar e recarregar:
   ```bash
   sudo nginx -t && sudo systemctl reload nginx
   ```

4. SSL com Certbot (se ainda não tiver certificado para api.automais.io):
   ```bash
   sudo certbot --nginx -d api.automais.io
   ```

## 3. DNS

O domínio **api.automais.io** deve apontar para o IP do servidor onde a API e o nginx estão (mesmo do automais.io ou o IP da API, conforme sua arquitetura).

## 4. Resumo

| Item | Verificação |
|------|-------------|
| API no servidor | `curl http://localhost:5000/health` → 200 |
| Rota de login | `curl -X POST http://localhost:5000/api/auth/login -H "Content-Type: application/json" -d '{}'` → 400 ou 401 (não 404) |
| Nginx api.automais.io | Arquivo em `sites-available` e link em `sites-enabled` |
| HTTPS | Certbot configurado para `api.automais.io` |

Depois disso, `POST https://api.automais.io/api/auth/login` deve deixar de retornar 404.
