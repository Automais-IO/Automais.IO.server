using Automais.Core.Entities;

namespace Automais.Core.DTOs;

/// <summary>
/// DTO para retorno de Router
/// </summary>
public class RouterDto
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? SerialNumber { get; set; }
    public string? Model { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? RouterOsApiUrl { get; set; }
    /// <summary>Usuário da API RouterOS no MikroTik.</summary>
    public string? ApiUsername { get; set; }
    /// <summary>Senha temporária (ex.: .rsc); após primeiro login bem-sucedido o serviço grava <see cref="ApiPassword"/> e limpa esta.</summary>
    public string? ApiPasswordTemporaria { get; set; }
    /// <summary>Senha definitiva da API (após rotação ou já cadastrada).</summary>
    public string? ApiPassword { get; set; }
    public Guid? VpnNetworkId { get; set; }
    /// <summary>
    /// Endpoint do servidor VPN associado (ex: "automais.io").
    /// Usado para construir a URL do WebSocket dinamicamente.
    /// </summary>
    public string? VpnNetworkServerEndpoint { get; set; }
    public RouterStatus Status { get; set; }
    /// <summary>API RouterOS (8728): Ok, falha de auth, inacessível ou não verificado.</summary>
    public RouterOsApiAuthStatus RouterOsApiAuthStatus { get; set; }
    public DateTime? RouterOsApiAuthCheckedAt { get; set; }
    public string? RouterOsApiAuthMessage { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int? Latency { get; set; }
    public string? HardwareInfo { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>
    /// Redes destino: redes para as quais o tráfego VPN é encaminhado via este router. Ex: ["10.0.1.0/24", "192.168.100.0/24"]
    /// </summary>
    public IEnumerable<string>? AllowedNetworks { get; set; }
    /// <summary>FK para <c>vpn_peers</c> (peer principal do router).</summary>
    public Guid? VpnPeerId { get; set; }
    /// <summary>True se o peer tem chave pública e privada preenchidas.</summary>
    public bool VpnPeerKeysConfigured { get; set; }
    /// <summary>IP do peer na VPN (extraído do PeerIp do peer). Endereço usado para conectar ao router (API/ping).</summary>
    public string? VpnTunnelIp { get; set; }
    /// <summary>Bytes recebidos pelo servidor deste peer (tráfego vindo do router).</summary>
    public long? VpnBytesReceived { get; set; }
    /// <summary>Bytes enviados pelo servidor para este peer.</summary>
    public long? VpnBytesSent { get; set; }
}

/// <summary>
/// DTO para criação de Router. Credenciais da API RouterOS e URL da API não vêm do cliente na criação.
/// </summary>
public class CreateRouterDto
{
    public string Name { get; set; } = string.Empty;
    /// <summary>
    /// OBSOLETO: SerialNumber é obtido automaticamente via API RouterOS na conexão
    /// </summary>
    [Obsolete("SerialNumber é obtido automaticamente via API RouterOS")]
    public string? SerialNumber { get; set; }
    /// <summary>
    /// OBSOLETO: Model é obtido automaticamente via API RouterOS na conexão
    /// </summary>
    [Obsolete("Model é obtido automaticamente via API RouterOS")]
    public string? Model { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public string? Description { get; set; }
    /// <summary>
    /// Ignorado na criação: redes destino são gerenciadas via CRUD em /routers/{id}/destination-networks após criar o router.
    /// </summary>
    public IEnumerable<string>? AllowedNetworks { get; set; }
    /// <summary>
    /// IP manual para o router na VPN (ex: "10.222.111.5/24"). Se não especificado, será alocado automaticamente.
    /// O IP .1 é sempre reservado para o servidor.
    /// </summary>
    public string? VpnIp { get; set; }
}

/// <summary>
/// DTO para atualização de Router
/// </summary>
public class UpdateRouterDto
{
    public string? Name { get; set; }
    /// <summary>
    /// OBSOLETO: SerialNumber não pode ser editado manualmente - é obtido via API RouterOS
    /// </summary>
    [Obsolete("SerialNumber não pode ser editado manualmente")]
    public string? SerialNumber { get; set; }
    /// <summary>
    /// OBSOLETO: Model não pode ser editado manualmente - é obtido via API RouterOS
    /// </summary>
    [Obsolete("Model não pode ser editado manualmente")]
    public string? Model { get; set; }
    public string? RouterOsApiUrl { get; set; }
    public string? ApiUsername { get; set; }
    public string? ApiPasswordTemporaria { get; set; }
    public Guid? VpnNetworkId { get; set; }
    public RouterStatus? Status { get; set; }
    public RouterOsApiAuthStatus? RouterOsApiAuthStatus { get; set; }
    public DateTime? RouterOsApiAuthCheckedAt { get; set; }
    public string? RouterOsApiAuthMessage { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public int? Latency { get; set; }
    public string? HardwareInfo { get; set; }
    public string? FirmwareVersion { get; set; }
    public string? Description { get; set; }
}

