namespace Automais.Core.DTOs;

/// <summary>
/// Rede permitida ao peer (split tunnel no cliente).
/// </summary>
public class AllowedNetworkDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public Guid VpnPeerId { get; set; }
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateAllowedNetworkDto
{
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateAllowedNetworkDto
{
    public string? NetworkCidr { get; set; }
    public string? Description { get; set; }
}
