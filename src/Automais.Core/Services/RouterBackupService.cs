using Automais.Core.DTOs;
using Automais.Core.Entities;
using Automais.Core.Interfaces;
using System.Security.Cryptography;
using System.Text;

namespace Automais.Core.Services;

/// <summary>
/// Serviço para gerenciamento de Backups dos Routers
/// </summary>
public class RouterBackupService : IRouterBackupService
{
    private readonly IRouterBackupRepository _backupRepository;
    private readonly IRouterRepository _routerRepository;
    private readonly ITenantUserRepository? _tenantUserRepository;
    private readonly string _backupStoragePath;

    public RouterBackupService(
        IRouterBackupRepository backupRepository,
        IRouterRepository routerRepository,
        ITenantUserRepository? tenantUserRepository = null,
        string backupStoragePath = "/backups/routers")
    {
        _backupRepository = backupRepository;
        _routerRepository = routerRepository;
        _tenantUserRepository = tenantUserRepository;
        _backupStoragePath = backupStoragePath;
    }

    public async Task<IEnumerable<RouterBackupDto>> GetByRouterIdAsync(Guid routerId, CancellationToken cancellationToken = default)
    {
        var backups = await _backupRepository.GetByRouterIdAsync(routerId, cancellationToken);
        return backups.Select(b => MapToDto(b));
    }

    public async Task<RouterBackupDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var backup = await _backupRepository.GetByIdAsync(id, cancellationToken);
        return backup == null ? null : MapToDto(backup);
    }

    public async Task<RouterBackupDto> CreateBackupAsync(Guid routerId, CreateRouterBackupDto dto, Guid? userId = null, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        }

        var hasApiPwd = !string.IsNullOrWhiteSpace(router.ApiPassword) || !string.IsNullOrWhiteSpace(router.ApiPasswordTemporaria);
        if (string.IsNullOrWhiteSpace(router.RouterOsApiUrl) ||
            string.IsNullOrWhiteSpace(router.ApiUsername) ||
            !hasApiPwd)
        {
            throw new InvalidOperationException("Credenciais da API RouterOS não configuradas.");
        }

        // Exportar configuração do router agora é feito via servidor VPN
        throw new NotImplementedException("CreateBackupAsync agora é feito via servidor VPN (WebSocket). Use o endpoint do servidor VPN.");

        // Código comentado - não será executado devido ao throw acima
        /*
        // Adicionar header com metadados
        var header = new StringBuilder();
        header.AppendLine("# RouterOS Configuration Export");
        header.AppendLine($"# Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        header.AppendLine($"# Router: {router.Name}");
        if (!string.IsNullOrWhiteSpace(router.Model))
        {
            header.AppendLine($"# Model: {router.Model}");
        }
        if (!string.IsNullOrWhiteSpace(router.FirmwareVersion))
        {
            header.AppendLine($"# RouterOS Version: {router.FirmwareVersion}");
        }
        header.AppendLine();

        var fullContent = header.ToString() + configContent;

        // Contar comandos (linhas que não são comentários ou vazias)
        var commandCount = fullContent.Split('\n')
            .Count(line => !string.IsNullOrWhiteSpace(line) && !line.TrimStart().StartsWith("#"));

        // Calcular hash
        var fileHash = CalculateSha256(fullContent);

        // Criar nome do arquivo
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"backup_{timestamp}.rsc";

        // Criar diretório se não existir
        var tenantBackupPath = Path.Combine(_backupStoragePath, router.TenantId.ToString());
        var routerBackupPath = Path.Combine(tenantBackupPath, routerId.ToString());
        Directory.CreateDirectory(routerBackupPath);

        // Salvar arquivo
        var filePath = Path.Combine(routerBackupPath, fileName);
        await File.WriteAllTextAsync(filePath, fullContent, cancellationToken);

        // Criar registro no banco
        var backup = new RouterBackup
        {
            Id = Guid.NewGuid(),
            RouterId = routerId,
            TenantId = router.TenantId,
            FileName = fileName,
            FilePath = filePath,
            FileSizeBytes = Encoding.UTF8.GetByteCount(fullContent),
            Description = dto.Description,
            RouterModel = router.Model,
            RouterOsVersion = router.FirmwareVersion,
            CommandCount = commandCount,
            FileHash = fileHash,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = userId,
            IsAutomatic = dto.IsAutomatic
        };

        var created = await _backupRepository.CreateAsync(backup, cancellationToken);
        return MapToDto(created);
        */
    }

    public async Task DeleteBackupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var backup = await _backupRepository.GetByIdAsync(id, cancellationToken);
        if (backup == null)
        {
            return;
        }

        // Deletar arquivo do filesystem
        if (File.Exists(backup.FilePath))
        {
            File.Delete(backup.FilePath);
        }

        // Deletar registro do banco
        await _backupRepository.DeleteAsync(id, cancellationToken);
    }

    public async Task<byte[]> DownloadBackupAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var backup = await _backupRepository.GetByIdAsync(id, cancellationToken);
        if (backup == null)
        {
            throw new KeyNotFoundException($"Backup com ID {id} não encontrado.");
        }

        if (!File.Exists(backup.FilePath))
        {
            throw new FileNotFoundException($"Arquivo de backup não encontrado: {backup.FilePath}");
        }

        return await File.ReadAllBytesAsync(backup.FilePath, cancellationToken);
    }

    public async Task<string> GetBackupContentAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var backup = await _backupRepository.GetByIdAsync(id, cancellationToken);
        if (backup == null)
        {
            throw new KeyNotFoundException($"Backup com ID {id} não encontrado.");
        }

        if (!File.Exists(backup.FilePath))
        {
            throw new FileNotFoundException($"Arquivo de backup não encontrado: {backup.FilePath}");
        }

        return await File.ReadAllTextAsync(backup.FilePath, cancellationToken);
    }

    public async Task<RouterBackupComparisonDto> CompareBackupAsync(Guid routerId, Guid backupId, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        }

        var backup = await _backupRepository.GetByIdAsync(backupId, cancellationToken);
        if (backup == null)
        {
            throw new KeyNotFoundException($"Backup com ID {backupId} não encontrado.");
        }

        if (backup.RouterId != routerId)
        {
            throw new InvalidOperationException("Backup não pertence a este router.");
        }

        // Exportar configuração atual agora é feito via servidor VPN
        throw new NotImplementedException("CompareBackupAsync agora é feito via servidor VPN (WebSocket). Use o endpoint do servidor VPN.");

        // Código comentado - não será executado devido ao throw acima
        /*
        // Ler backup
        var backupContent = await GetBackupContentAsync(backupId, cancellationToken);

        // Comparar (implementação básica - pode ser melhorada)
        var differences = CompareConfigs(currentConfig, backupContent);
        var unifiedDiff = GenerateUnifiedDiff(currentConfig, backupContent);

        return new RouterBackupComparisonDto
        {
            BackupId = backupId,
            BackupFileName = backup.FileName,
            BackupCreatedAt = backup.CreatedAt,
            CommandsToAdd = differences.Count(d => d.Type == "add"),
            CommandsToRemove = differences.Count(d => d.Type == "remove"),
            CommandsToModify = differences.Count(d => d.Type == "modify"),
            Differences = differences,
            UnifiedDiff = unifiedDiff
        };
        */
    }

    public async Task RestoreBackupAsync(Guid routerId, Guid backupId, RestoreRouterBackupDto dto, CancellationToken cancellationToken = default)
    {
        var router = await _routerRepository.GetByIdAsync(routerId, cancellationToken);
        if (router == null)
        {
            throw new KeyNotFoundException($"Router com ID {routerId} não encontrado.");
        }

        var backup = await _backupRepository.GetByIdAsync(backupId, cancellationToken);
        if (backup == null)
        {
            throw new KeyNotFoundException($"Backup com ID {backupId} não encontrado.");
        }

        if (backup.RouterId != routerId)
        {
            throw new InvalidOperationException("Backup não pertence a este router.");
        }

        // Ler conteúdo do backup (remover header de metadados)
        var backupContent = await GetBackupContentAsync(backupId, cancellationToken);
        var configContent = RemoveHeader(backupContent);

        if (dto.DryRun)
        {
            // Apenas validar, não aplicar
            // TODO: Implementar validação de comandos
            return;
        }

        // Aplicar configuração agora é feito via servidor VPN
        throw new NotImplementedException("RestoreBackupAsync agora é feito via servidor VPN (WebSocket). Use o endpoint do servidor VPN.");
    }

    private List<ConfigDifferenceDto> CompareConfigs(string current, string backup)
    {
        // Implementação básica de comparação
        // TODO: Implementar comparação mais sofisticada
        var differences = new List<ConfigDifferenceDto>();
        
        var currentLines = current.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.TrimStart().StartsWith("#"))
            .ToList();
        var backupLines = backup.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(l => !l.TrimStart().StartsWith("#"))
            .ToList();

        // Comandos a adicionar (estão no backup mas não no atual)
        foreach (var backupLine in backupLines)
        {
            if (!currentLines.Contains(backupLine))
            {
                differences.Add(new ConfigDifferenceDto
                {
                    Type = "add",
                    Category = ExtractCategory(backupLine),
                    Path = ExtractPath(backupLine),
                    BackupValue = backupLine,
                    Command = backupLine
                });
            }
        }

        // Comandos a remover (estão no atual mas não no backup)
        foreach (var currentLine in currentLines)
        {
            if (!backupLines.Contains(currentLine))
            {
                differences.Add(new ConfigDifferenceDto
                {
                    Type = "remove",
                    Category = ExtractCategory(currentLine),
                    Path = ExtractPath(currentLine),
                    CurrentValue = currentLine,
                    Command = currentLine
                });
            }
        }

        return differences;
    }

    private string GenerateUnifiedDiff(string current, string backup)
    {
        // Implementação básica de diff unificado
        // TODO: Usar biblioteca de diff (ex: DiffPlex)
        var sb = new StringBuilder();
        sb.AppendLine("@@ -1,1 +1,1 @@");
        sb.AppendLine("- Configuração atual");
        sb.AppendLine("+ Configuração do backup");
        return sb.ToString();
    }

    private string ExtractCategory(string command)
    {
        // Extrai categoria do comando (ex: "/ip/firewall" -> "firewall")
        var parts = command.Trim().Split('/');
        return parts.Length > 2 ? parts[2] : "unknown";
    }

    private string ExtractPath(string command)
    {
        // Extrai caminho do comando (ex: "/ip/firewall/filter/rule/5")
        var parts = command.Trim().Split(' ');
        return parts.Length > 0 ? parts[0] : "";
    }

    private string RemoveHeader(string content)
    {
        // Remove linhas de comentário do header
        var lines = content.Split('\n');
        var configLines = lines.Where(l => !l.TrimStart().StartsWith("#")).ToList();
        return string.Join("\n", configLines);
    }

    private static string CalculateSha256(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLower();
    }

    private RouterBackupDto MapToDto(RouterBackup backup)
    {
        return new RouterBackupDto
        {
            Id = backup.Id,
            RouterId = backup.RouterId,
            TenantId = backup.TenantId,
            FileName = backup.FileName,
            FileSizeBytes = backup.FileSizeBytes,
            Description = backup.Description,
            RouterModel = backup.RouterModel,
            RouterOsVersion = backup.RouterOsVersion,
            CommandCount = backup.CommandCount,
            FileHash = backup.FileHash,
            CreatedAt = backup.CreatedAt,
            CreatedByUserId = backup.CreatedByUserId,
            CreatedByUserName = null, // TODO: Buscar nome do usuário se necessário
            IsAutomatic = backup.IsAutomatic
        };
    }
}

