namespace Automais.Core.DTOs;

public class RemoteNetworkDto
{
    public Guid Id { get; set; }
    public Guid RouterId { get; set; }
    public Guid VpnPeerId { get; set; }
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateRemoteNetworkDto
{
    public string NetworkCidr { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdateRemoteNetworkDto
{
    public string? NetworkCidr { get; set; }
    public string? Description { get; set; }
}
