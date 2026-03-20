namespace Automais.Core.DTOs;

/// <summary>Corpo do POST público que o script de bootstrap envia ao concluir.</summary>
public class SetupCompleteRequestDto
{
    public string Token { get; set; } = string.Empty;
}
