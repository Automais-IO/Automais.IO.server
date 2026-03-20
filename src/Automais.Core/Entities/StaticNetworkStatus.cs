namespace Automais.Core.Entities;

/// <summary>
/// Status da aplicação no RouterOS (tabela <c>static_networks</c>).
/// </summary>
public enum StaticNetworkStatus
{
    PendingAdd = 1,
    PendingRemove = 2,
    Applied = 3,
    Error = 4
}
