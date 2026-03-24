namespace Automais.Core.DTOs;

public class ValidateWebDeviceAgentRequestDto
{
    public string Token { get; set; } = string.Empty;
}

public class ValidateWebDeviceAgentResponseDto
{
    public bool Valid { get; set; }
    public Guid TenantId { get; set; }
    public bool WebDeviceEnabled { get; set; }
}
