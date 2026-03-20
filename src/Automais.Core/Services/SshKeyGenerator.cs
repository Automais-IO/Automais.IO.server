using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Automais.Core.Services;

/// <summary>
/// Gera par de chaves SSH Ed25519 via ssh-keygen.
/// </summary>
public static class SshKeyGenerator
{
    public static async Task<(string PrivateKey, string PublicKey)> GenerateEd25519KeyPairAsync(
        CancellationToken cancellationToken = default)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "automais_ssh_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        var keyPath = Path.Combine(tmpDir, "id_ed25519");

        try
        {
            var sshKeygen = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ssh-keygen" : "/usr/bin/ssh-keygen";
            var psi = new ProcessStartInfo
            {
                FileName = sshKeygen,
                Arguments = $"-t ed25519 -f \"{keyPath}\" -N \"\" -C automais-io -q",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Não foi possível iniciar ssh-keygen");
            await proc.WaitForExitAsync(cancellationToken);

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException($"ssh-keygen falhou (exit {proc.ExitCode}): {err}");
            }

            var privateKey = await File.ReadAllTextAsync(keyPath, cancellationToken);
            var publicKey = await File.ReadAllTextAsync(keyPath + ".pub", cancellationToken);

            return (privateKey.Trim(), publicKey.Trim());
        }
        finally
        {
            try { Directory.Delete(tmpDir, recursive: true); } catch { }
        }
    }
}
