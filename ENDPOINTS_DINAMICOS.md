# Endpoints Dinâmicos - Sistema de Múltiplos Servidores VPN

## Visão Geral

O sistema suporta múltiplos servidores VPN através de **endpoints dinâmicos** baseados no campo `ServerEndpoint` da `VpnNetwork`. Cada router pode estar associado a uma VpnNetwork diferente, que por sua vez pode ter um `ServerEndpoint` diferente, permitindo que diferentes routers se conectem a diferentes servidores VPN físicos.

## Como Funciona

### 1. Estrutura de Dados

- **Router**: Possui um campo `VpnNetworkId` que identifica a VpnNetwork associada
- **VpnNetwork**: Possui um campo `ServerEndpoint` (ex: "automais.io", "vpn2.automais.io") que identifica qual servidor VPN físico gerencia aquela rede
- **RouterDto**: Inclui `VpnNetworkServerEndpoint` para facilitar o acesso no frontend

### 2. Construção de URLs

#### Frontend (WebSocket)

No frontend, a URL do WebSocket é construída dinamicamente baseada no `vpnNetworkServerEndpoint` do router:

```javascript
// front.io/src/config/api.js
export const getRouterOsWsUrl = (serverEndpoint) => {
  if (!serverEndpoint) {
    // Fallback para URL padrão
    return isProduction ? 'ws://automais.io:8765' : 'ws://localhost:8765'
  }
  
  // Construir URL: ws://{ServerEndpoint}:8765
  return `ws://${serverEndpoint}:8765`
}
```

**Uso:**
```javascript
// front.io/src/pages/Routers/RouterManagement.jsx
const wsUrl = getRouterOsWsUrl(routerData.vpnNetworkServerEndpoint)
await routerOsWebSocketService.connect(wsUrl)
```

#### Backend (HTTP API e WebSocket)

No backend, as URLs são construídas dinamicamente em cada chamada:

##### VpnServiceClient (HTTP API)

O `VpnServiceClient` busca o `ServerEndpoint` da VpnNetwork antes de fazer cada chamada HTTP:

```csharp
// server.io/src/Automais.Infrastructure/Services/VpnServiceClient.cs

// Buscar ServerEndpoint da VpnNetwork
private async Task<string?> GetServerEndpointAsync(Guid vpnNetworkId, CancellationToken cancellationToken)
{
    var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(vpnNetworkId, cancellationToken);
    return vpnNetwork?.ServerEndpoint;
}

// Construir URL base
private string GetBaseUrl(string? serverEndpoint = null)
{
    if (string.IsNullOrWhiteSpace(serverEndpoint))
    {
        return _options.BaseUrl; // Fallback
    }
    
    // Construir: http://{ServerEndpoint}:8000
    return $"http://{serverEndpoint}:8000";
}

// Usar em cada chamada
var serverEndpoint = await GetServerEndpointAsync(vpnNetworkId, cancellationToken);
var baseUrl = GetBaseUrl(serverEndpoint);
var fullUrl = $"{baseUrl}/api/v1/vpn/provision-peer";
```

##### RouterOsWebSocketClient (WebSocket)

O `RouterOsWebSocketClient` também busca o `ServerEndpoint` antes de conectar:

```csharp
// server.io/src/Automais.Infrastructure/Services/RouterOsWebSocketClient.cs

// Buscar ServerEndpoint do router via VpnNetwork
string? serverEndpoint = null;
if (router.VpnNetworkId.HasValue && _vpnNetworkRepository != null)
{
    var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(router.VpnNetworkId.Value, cancellationToken);
    serverEndpoint = vpnNetwork?.ServerEndpoint;
}

// Construir URL: ws://{ServerEndpoint}:8765
var wsUrl = GetWebSocketUrl(serverEndpoint);
```

## Formato dos Endpoints

### ServerEndpoint na VpnNetwork

O campo `ServerEndpoint` pode ser especificado de várias formas:

1. **Apenas hostname**: `"automais.io"` → Constrói `http://automais.io:8000` ou `ws://automais.io:8765`
2. **Com protocolo**: `"http://automais.io"` → Constrói `http://automais.io:8000`
3. **Com protocolo e porta**: `"http://automais.io:8000"` → Usa como está

### Portas Padrão

- **HTTP API (VpnServiceClient)**: Porta `8000`
- **WebSocket (RouterOsWebSocketClient)**: Porta `8765`

## Fluxo de Funcionamento

### 1. Frontend - Conexão WebSocket

```
1. Usuário acessa RouterManagement
2. Sistema carrega router via API: GET /api/routers/{id}
3. RouterDto retorna com vpnNetworkServerEndpoint
4. Frontend constrói URL: ws://{vpnNetworkServerEndpoint}:8765
5. Conecta ao WebSocket do servidor VPN correto
```

### 2. Backend - Chamadas HTTP

```
1. Backend recebe requisição (ex: ProvisionPeerAsync)
2. Método recebe vpnNetworkId
3. Busca VpnNetwork no banco
4. Obtém ServerEndpoint
5. Constrói URL: http://{ServerEndpoint}:8000/api/v1/vpn/...
6. Faz chamada HTTP ao servidor VPN correto
```

### 3. Backend - Conexão WebSocket

```
1. Backend recebe requisição GetConnectionStatusAsync
2. Busca router no banco
3. Obtém VpnNetworkId do router
4. Busca VpnNetwork e obtém ServerEndpoint
5. Constrói URL: ws://{ServerEndpoint}:8765
6. Conecta ao WebSocket do servidor VPN correto
```

## Fallback

Se o `ServerEndpoint` não estiver configurado ou não for encontrado:

- **Frontend**: Usa URL padrão da configuração (`ws://automais.io:8765` ou `ws://localhost:8765`)
- **Backend**: Usa URL padrão do `appsettings.json` (`http://localhost:8000` ou `ws://localhost:8765`)

## Exemplo de Uso

### Cenário: Múltiplos Servidores VPN

```
VpnNetwork 1:
  - Name: "Rede Principal"
  - ServerEndpoint: "automais.io"
  - Routers associados usam: http://automais.io:8000 e ws://automais.io:8765

VpnNetwork 2:
  - Name: "Rede Secundária"
  - ServerEndpoint: "vpn2.automais.io"
  - Routers associados usam: http://vpn2.automais.io:8000 e ws://vpn2.automais.io:8765
```

### Router A (associado à VpnNetwork 1)
- Frontend conecta: `ws://automais.io:8765`
- Backend chama: `http://automais.io:8000/api/v1/vpn/...`

### Router B (associado à VpnNetwork 2)
- Frontend conecta: `ws://vpn2.automais.io:8765`
- Backend chama: `http://vpn2.automais.io:8000/api/v1/vpn/...`

## Importante

⚠️ **NUNCA configure BaseAddress no HttpClient** quando usar endpoints dinâmicos. As URLs devem ser construídas em cada chamada baseadas no `ServerEndpoint` da VpnNetwork.

⚠️ **Sempre busque o ServerEndpoint** antes de fazer chamadas HTTP ou conectar WebSocket. O ServerEndpoint identifica qual servidor VPN físico gerencia aquela rede.

## Referências

- `VpnNetwork.ServerEndpoint`: Campo que identifica o servidor VPN
- `RouterDto.VpnNetworkServerEndpoint`: Campo incluído no DTO para facilitar acesso no frontend
- `VpnServiceClient.GetBaseUrl()`: Método que constrói URL HTTP baseada no ServerEndpoint
- `RouterOsWebSocketClient.GetWebSocketUrl()`: Método que constrói URL WebSocket baseada no ServerEndpoint
- `getRouterOsWsUrl()`: Função JavaScript que constrói URL WebSocket no frontend
