namespace Automais.Core;

/// <summary>Normaliza DevEUI para comparação com a coluna (hex uppercase, ignora separadores comuns).</summary>
public static class DevEuiNormalizer
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static bool TryNormalize(string? raw, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        Span<char> buffer = stackalloc char[MaxLength];
        var len = 0;
        foreach (var c in raw.Trim())
        {
            if (c is ' ' or '-' or ':' or '.')
                continue;
            if (!Uri.IsHexDigit(c))
                return false;
            if (len >= MaxLength)
                return false;
            buffer[len++] = char.ToUpperInvariant(c);
        }

        if (len < MinLength)
            return false;

        normalized = new string(buffer[..len]);
        return true;
    }
}
