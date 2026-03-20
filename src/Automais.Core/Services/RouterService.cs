using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Automais.Core.Services;

/// <summary>
/// Serviço de lógica de negócio para Routers
/// </summary>
public class RouterService : IRouterService
{
    private readonly IRouterRepository _routerRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IAllowedNetworkRepository? _allowedNetworkRepository;
    private readonly IVpnPeerService? _vpnPeerService;
    private readonly IVpnNetworkRepository? _vpnNetworkRepository;
    private readonly IVpnPeerRepository? _vpnPeerRepository;
    private readonly ILogger<RouterService>? _logger;

    public RouterService(
        IRouterRepository routerRepository,
        ITenantRepository tenantRepository,
        IAllowedNetworkRepository? allowedNetworkRepository = null,
        IVpnPeerService? vpnPeerService = null,
        IVpnNetworkRepository? vpnNetworkRepository = null,
        IVpnPeerRepository? vpnPeerRepository = null,
        ILogger<RouterService>? logger = null)
    {
        _routerRepository = routerRepository;
        _tenantRepository = tenantRepository;
        _allowedNetworkRepository = allowedNetworkRepository;
        _vpnPeerService = vpnPeerService;
        _vpnNetworkRepository = vpnNetworkRepository;
        _vpnPeerRepository = vpnPeerRepository;
        _logger = logger;
    }

    public async Task<IEnumerable<RouterDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var routers = await _routerRepository.GetAllAsync(cancellationToken);
            var result = new List<RouterDto>();
            foreach (var router in routers)
            {
                result.Add(await MapToDtoAsync(router, cancellationToken));
            }
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao buscar todos os routers: {ex.Message}", ex);
        }
    }

    public async Task<IEnumerable<RouterDto>> GetByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            // Buscar routers diretamente sem verificar tenant primeiro
            // Isso evita JOINs desnecessários que podem causar problemas com snake_case
            var routers = await _routerRepository.GetByTenantIdAsync(tenantId, cancellationToken);
            var result = new List<RouterDto>();
            foreach (var router in routers)
            {
                result.Add(await MapToDtoAsync(router, cancellationToken));
            }
            return result;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Erro ao buscar routers do tenant {tenantId}: {ex.Message}", ex);
        }
    }

    public async Task<RouterDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null) return null;
        
        return await MapToDtoAsync(router, cancellationToken);
    }

    public async Task<RouterDto> CreateAsync(Guid tenantId, CreateRouterDto dto, string apiUsername, string apiPasswordTemporaria, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantRepository.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new KeyNotFoundException($"Tenant com ID {tenantId} não encontrado.");
        }

        // Validar VpnNetworkId se fornecido
        if (dto.VpnNetworkId.HasValue)
        {
            // TODO: Adicionar IVpnNetworkRepository para validar se a rede VPN existe
            // Por enquanto, a validação será feita pelo Entity Framework (foreign key constraint)
        }

        var router = new Router
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = dto.Name,
            // SerialNumber, Model e FirmwareVersion serão preenchidos automaticamente via API RouterOS
            SerialNumber = null,
            Model = null,
            FirmwareVersion = null,
            RouterOsApiUrl = null,
            ApiUsername = apiUsername,
            ApiPasswordTemporaria = apiPasswordTemporaria,
            VpnNetworkId = dto.VpnNetworkId,
            Description = dto.Description,
            Status = RouterStatus.Offline,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var created = await _routerRepository.CreateAsync(router, cancellationToken);
        
        // Se tem VpnNetworkId, provisionar peer VPN automaticamente.
        // PeerIp = apenas IP do router (ou vazio para alocação automática). Redes destino são adicionadas depois via CRUD.
        if (created.VpnNetworkId.HasValue && _vpnPeerService != null)
        {
            try
            {
                var peerIp = !string.IsNullOrWhiteSpace(dto.VpnIp) ? dto.VpnIp : string.Empty;
                var peerDto = new CreateVpnPeerDto
                {
                    VpnNetworkId = created.VpnNetworkId.Value,
                    PeerIp = peerIp
                };

                await _vpnPeerService.CreatePeerAsync(created.Id, peerDto, cancellationToken);
            }
            catch (Exception)
            {
                // Logar erro mas não falhar a criação do router; o peer pode ser criado manualmente depois
            }
        }
        
        return await MapToDtoAsync(created, cancellationToken);
    }

    public async Task<RouterDto> UpdateAsync(Guid id, UpdateRouterDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {id} não encontrado.");
        }

        // Log dos dados recebidos para debug
        _logger?.LogInformation($"📥 [SERVICE] Iniciando atualização do router {id}");
        _logger?.LogInformation($"   Status: {dto.Status}");
        _logger?.LogInformation($"   LastSeenAt: {dto.LastSeenAt}");
        _logger?.LogInformation($"   Latency: {dto.Latency}");
        _logger?.LogInformation($"   HardwareInfo: {(dto.HardwareInfo != null ? $"presente ({dto.HardwareInfo.Length} chars)" : "null")}");
        _logger?.LogInformation($"   FirmwareVersion: {dto.FirmwareVersion}");
        _logger?.LogInformation($"   Model: {dto.Model}");
        
        // Log do JSON completo do DTO
        try
        {
            var jsonDto = JsonSerializer.Serialize(dto, new JsonSerializerOptions 
            { 
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            _logger?.LogInformation($"   📋 DTO completo recebido (JSON):\n{jsonDto}");
        }
        catch (Exception jsonEx)
        {
            _logger?.LogWarning(jsonEx, "   ⚠️ Erro ao serializar DTO para log");
        }

        if (!string.IsNullOrWhiteSpace(dto.Name))
        {
            router.Name = dto.Name;
        }

        // SerialNumber, Model e FirmwareVersion não podem ser editados manualmente
        // Eles são atualizados automaticamente via API RouterOS quando conecta

        if (dto.RouterOsApiUrl != null)
        {
            router.RouterOsApiUrl = dto.RouterOsApiUrl;
        }

        if (dto.ApiUsername != null)
            router.ApiUsername = dto.ApiUsername;

        if (dto.ApiPasswordTemporaria != null)
            router.ApiPasswordTemporaria = dto.ApiPasswordTemporaria;

        if (dto.VpnNetworkId.HasValue)
        {
            router.VpnNetworkId = dto.VpnNetworkId.Value;
        }

        if (dto.Status.HasValue)
        {
            router.Status = dto.Status.Value;
            _logger?.LogDebug($"✅ Status atualizado para {router.Status}");
        }

        if (dto.RouterOsApiAuthStatus.HasValue)
        {
            router.RouterOsApiAuthStatus = dto.RouterOsApiAuthStatus.Value;
            _logger?.LogDebug("Router {RouterId}: RouterOsApiAuthStatus atualizado para {Status}", id, router.RouterOsApiAuthStatus);
        }

        if (dto.RouterOsApiAuthCheckedAt.HasValue)
        {
            router.RouterOsApiAuthCheckedAt = ToPostgreSqlUtc(dto.RouterOsApiAuthCheckedAt.Value);
        }

        if (dto.RouterOsApiAuthMessage != null)
        {
            router.RouterOsApiAuthMessage = string.IsNullOrWhiteSpace(dto.RouterOsApiAuthMessage)
                ? null
                : dto.RouterOsApiAuthMessage.Length > 500
                    ? dto.RouterOsApiAuthMessage[..500]
                    : dto.RouterOsApiAuthMessage;
        }

        // Log consolidado quando a tabela router for atualizada com informações de última conexão/resultado da API RouterOS
        if (dto.RouterOsApiAuthStatus.HasValue || dto.RouterOsApiAuthCheckedAt.HasValue || dto.RouterOsApiAuthMessage != null)
        {
            _logger?.LogInformation(
                "Router {RouterId}: tabela router atualizada com informações de acesso à API RouterOS — Status={Status}, VerificadoEm={CheckedAt}, Mensagem={Message}",
                id, router.RouterOsApiAuthStatus, router.RouterOsApiAuthCheckedAt, router.RouterOsApiAuthMessage ?? "(nula)");
        }

        if (dto.LastSeenAt.HasValue)
        {
            router.LastSeenAt = ToPostgreSqlUtc(dto.LastSeenAt.Value);
            _logger?.LogDebug($"✅ LastSeenAt atualizado para {router.LastSeenAt}");
        }

        if (dto.Latency.HasValue)
        {
            router.Latency = dto.Latency.Value;
            _logger?.LogDebug($"✅ Latency atualizado para {router.Latency}");
        }

        if (dto.HardwareInfo != null)
        {
            router.HardwareInfo = dto.HardwareInfo;
            _logger?.LogDebug($"✅ HardwareInfo atualizado (tamanho: {dto.HardwareInfo.Length} chars)");
        }

        if (dto.FirmwareVersion != null)
        {
            router.FirmwareVersion = dto.FirmwareVersion;
            _logger?.LogDebug($"✅ FirmwareVersion atualizado para {router.FirmwareVersion}");
        }

        if (dto.Model != null)
        {
            router.Model = dto.Model;
            _logger?.LogDebug($"✅ Model atualizado para {router.Model}");
        }

        if (dto.Description != null)
        {
            router.Description = dto.Description;
        }

        router.UpdatedAt = DateTime.UtcNow;

        // Log do estado final antes de salvar
        _logger?.LogInformation($"💾 [SERVICE] Salvando router {id} no banco:");
        _logger?.LogInformation($"   Status final: {router.Status}");
        _logger?.LogInformation($"   LastSeenAt final: {router.LastSeenAt}");
        _logger?.LogInformation($"   Latency final: {router.Latency}");
        _logger?.LogInformation($"   HardwareInfo final: {(router.HardwareInfo != null ? $"presente ({router.HardwareInfo.Length} chars)" : "null")}");
        _logger?.LogInformation($"   FirmwareVersion final: {router.FirmwareVersion}");
        _logger?.LogInformation($"   Model final: {router.Model}");

        var updated = await _routerRepository.UpdateAsync(router, cancellationToken);
        _logger?.LogInformation($"✅ [SERVICE] Router {id} salvo no banco com sucesso");
        return await MapToDtoAsync(updated, cancellationToken);
    }

    /// <summary>
    /// Npgsql / PostgreSQL timestamptz só aceita DateTime Kind=Utc.
    /// JSON (vpnserver, routeros) pode deserializar como Local ou Unspecified.
    /// </summary>
    private static DateTime ToPostgreSqlUtc(DateTime dt) => dt.Kind switch
    {
        DateTimeKind.Utc => dt,
        DateTimeKind.Local => dt.ToUniversalTime(),
        _ => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
    };

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null)
        {
            return;
        }

        if (router.VpnPeerId.HasValue && _vpnPeerRepository != null)
        {
            await _vpnPeerRepository.DeleteAsync(router.VpnPeerId.Value, cancellationToken);
        }

        await _routerRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<RouterDto> TestConnectionAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Teste de conexão agora é feito via servidor VPN (WebSocket)
        // Este método é mantido para compatibilidade, mas não faz nada
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {id} não encontrado.");
        }

        throw new NotImplementedException("TestConnectionAsync agora é feito via servidor VPN (WebSocket). Use o endpoint do servidor VPN.");
    }

    private async Task<RouterDto> MapToDtoAsync(Router router, CancellationToken cancellationToken = default)
    {
        // Buscar redes destino se houver repositório disponível
        IEnumerable<string>? allowedNetworks = null;
        if (_allowedNetworkRepository != null)
        {
            try
            {
                var networks = await _allowedNetworkRepository.GetByRouterIdAsync(router.Id, cancellationToken);
                allowedNetworks = networks.Select(n => n.NetworkCidr).ToList();
            }
            catch
            {
                // Se falhar, deixa como null
                allowedNetworks = null;
            }
        }

        // Buscar ServerEndpoint da VpnNetwork se houver repositório disponível
        string? vpnNetworkServerEndpoint = null;
        Guid? vpnPeerId = router.VpnPeerId;
        var vpnPeerKeysConfigured = false;
        if (router.VpnNetworkId.HasValue && _vpnNetworkRepository != null)
        {
            try
            {
                var vpnNetwork = await _vpnNetworkRepository.GetByIdAsync(router.VpnNetworkId.Value, cancellationToken);
                vpnNetworkServerEndpoint = vpnNetwork?.ServerEndpoint;
            }
            catch
            {
                vpnNetworkServerEndpoint = null;
            }
        }

        string? vpnTunnelIp = null;
        long? vpnBytesReceived = null;
        long? vpnBytesSent = null;
        if (_vpnPeerRepository != null)
        {
            try
            {
                VpnPeer? peer = null;
                if (vpnPeerId.HasValue)
                    peer = await _vpnPeerRepository.GetByIdAsync(vpnPeerId.Value, cancellationToken);
                if (peer == null && router.VpnNetworkId.HasValue)
                    peer = await _vpnPeerRepository.GetByRouterIdAndNetworkIdAsync(
                        router.Id, router.VpnNetworkId.Value, cancellationToken);

                if (peer != null)
                {
                    vpnPeerId = peer.Id;
                    vpnPeerKeysConfigured = !string.IsNullOrWhiteSpace(peer.PublicKey)
                                                  && !string.IsNullOrWhiteSpace(peer.PrivateKey);
                    vpnTunnelIp = ExtractVpnTunnelIp(peer.PeerIp);
                    vpnBytesReceived = peer.BytesReceived;
                    vpnBytesSent = peer.BytesSent;
                }
            }
            catch
            {
                // ignora
            }
        }

        return new RouterDto
        {
            Id = router.Id,
            TenantId = router.TenantId,
            Name = router.Name,
            SerialNumber = router.SerialNumber,
            Model = router.Model,
            FirmwareVersion = router.FirmwareVersion,
            RouterOsApiUrl = router.RouterOsApiUrl,
            ApiUsername = router.ApiUsername,
            ApiPasswordTemporaria = router.ApiPasswordTemporaria,
            ApiPassword = router.ApiPassword,
            VpnNetworkId = router.VpnNetworkId,
            VpnNetworkServerEndpoint = vpnNetworkServerEndpoint,
            Status = router.Status,
            RouterOsApiAuthStatus = router.RouterOsApiAuthStatus,
            RouterOsApiAuthCheckedAt = router.RouterOsApiAuthCheckedAt,
            RouterOsApiAuthMessage = router.RouterOsApiAuthMessage,
            LastSeenAt = router.LastSeenAt,
            Latency = router.Latency,
            HardwareInfo = router.HardwareInfo,
            Description = router.Description,
            CreatedAt = router.CreatedAt,
            UpdatedAt = router.UpdatedAt,
            AllowedNetworks = allowedNetworks,
            VpnPeerId = vpnPeerId,
            VpnPeerKeysConfigured = vpnPeerKeysConfigured,
            VpnTunnelIp = vpnTunnelIp,
            VpnBytesReceived = vpnBytesReceived,
            VpnBytesSent = vpnBytesSent
        };
    }

    private static string? ExtractVpnTunnelIp(string? peerIp)
    {
        if (string.IsNullOrWhiteSpace(peerIp)) return null;
        var first = peerIp.Split(',')[0].Trim();
        var slash = first.IndexOf('/');
        return slash > 0 ? first[..slash] : (string.IsNullOrEmpty(first) ? null : first);
    }

    public async Task<RouterDto> UpdateSystemInfoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Atualização de informações do sistema agora é feita via servidor VPN (WebSocket)
        // Este método é mantido para compatibilidade, mas não faz nada
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {id} não encontrado");
        }

        throw new NotImplementedException("UpdateSystemInfoAsync agora é feito via servidor VPN (WebSocket). Use o endpoint do servidor VPN.");
    }

    public async Task UpdatePasswordAsync(Guid id, string newPassword, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(id, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {id} não encontrado");
        }

        router.ApiPasswordTemporaria = null;
        router.ApiPassword = newPassword;
        router.UpdatedAt = DateTime.UtcNow;

        await _routerRepository.UpdateAsync(router, cancellationToken);
        
        // Senha do router atualizada: ApiPasswordTemporaria=null, ApiPassword=nova senha
    }
}

