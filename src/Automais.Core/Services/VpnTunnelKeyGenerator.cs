using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Automais.Core.Services;

/// <summary>
/// Gera par de chaves do túnel VPN (ferramenta <c>wg</c> no sistema).
/// </summary>
public static class VpnTunnelKeyGenerator
{
    private static string WgExecutable =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "wg" : "/usr/bin/wg";

    public static async Task<(string PublicKey, string PrivateKey)> GenerateKeyPairAsync(
        CancellationToken cancellationToken = default)
    {
        var wg = WgExecutable;

        var privateKeyPsi = new ProcessStartInfo
        {
            FileName = wg,
            Arguments = "genkey",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var privateKeyProcess = Process.Start(privateKeyPsi)
            ?? throw new InvalidOperationException("Não foi possível iniciar wg genkey");

        var privateKey = (await privateKeyProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await privateKeyProcess.WaitForExitAsync(cancellationToken);

        if (privateKeyProcess.ExitCode != 0 || string.IsNullOrEmpty(privateKey))
        {
            throw new InvalidOperationException(
                "Erro ao gerar chave privada do túnel VPN (wg genkey). Verifique se as ferramentas VPN estão instaladas no servidor da API.");
        }

        var publicKeyPsi = new ProcessStartInfo
        {
            FileName = wg,
            Arguments = "pubkey",
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var publicKeyProcess = Process.Start(publicKeyPsi)
            ?? throw new InvalidOperationException("Não foi possível iniciar wg pubkey");

        await publicKeyProcess.StandardInput.WriteAsync(privateKey);
        await publicKeyProcess.StandardInput.FlushAsync();
        publicKeyProcess.StandardInput.Close();

        var publicKey = (await publicKeyProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await publicKeyProcess.WaitForExitAsync(cancellationToken);

        if (publicKeyProcess.ExitCode != 0 || string.IsNullOrEmpty(publicKey))
        {
            throw new InvalidOperationException("Erro ao gerar chave pública do túnel VPN (wg pubkey).");
        }

        return (publicKey, privateKey);
    }
}
