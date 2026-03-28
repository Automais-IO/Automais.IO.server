#Requires -Version 5.1
<#
.SYNOPSIS
  Publica docs\automais.io.env em /root/automais.io/automais.io.env no servidor.

.DESCRIPTION
  Credenciais em docs\credenciaisssh.env (gitignored):
    HOST - hostname ou IP
    USER - usuário SSH
    KEY  - caminho absoluto do arquivo .key (pode conter espacos) ou chave PEM multilinha (BEGIN...END)

  Fluxo: scp para /tmp, backup do ficheiro atual, copia para /root/automais.io/;
  se o destino ficar vazio apos copia, restaura backup. Depois systemctl restart
  de todos os units Automais.IO que usam EnvironmentFile=-/root/automais.io/automais.io.env.
#>
param(
    [string]$CredentialsFile,
    [string]$LocalEnvFile
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path

if (-not $CredentialsFile) {
    $CredentialsFile = Join-Path $repoRoot 'docs\credenciaisssh.env'
}
if (-not $LocalEnvFile) {
    $LocalEnvFile = Join-Path $repoRoot 'docs\automais.io.env'
}
if (-not (Test-Path $CredentialsFile)) {
    throw "Arquivo de credenciais nao encontrado: $CredentialsFile"
}
if (-not (Test-Path -LiteralPath $LocalEnvFile)) {
    throw "Arquivo local nao encontrado: $LocalEnvFile"
}

$cred = @{}
$lines = @(Get-Content -LiteralPath $CredentialsFile -Encoding UTF8)
$li = 0
while ($li -lt $lines.Count) {
    $t = $lines[$li].Trim()
    if ($t -eq '' -or $t.StartsWith('#')) { $li++; continue }
    $i = $t.IndexOf('=')
    if ($i -lt 1) { $li++; continue }
    $k = $t.Substring(0, $i).Trim()
    $v = $t.Substring($i + 1).Trim()
    if ($k -eq 'KEY' -and $v.StartsWith('-----BEGIN')) {
        $parts = [System.Collections.Generic.List[string]]::new()
        [void]$parts.Add($v)
        $li++
        while ($li -lt $lines.Count) {
            $row = $lines[$li].Trim()
            [void]$parts.Add($row)
            if ($row.StartsWith('-----END')) { break }
            $li++
        }
        $cred['KEY'] = ($parts -join "`n") + "`n"
        $li++
        continue
    }
    if ($v.StartsWith('"') -and $v.EndsWith('"')) { $v = $v.Substring(1, $v.Length - 2) }
    $cred[$k] = $v
    $li++
}

function Get-Cred([string]$name) {
    if ($cred.ContainsKey($name) -and $cred[$name] -ne '') { return $cred[$name] }
    return $null
}

function Repair-OpenSshPrivateKeyAcl {
    param([Parameter(Mandatory)][string]$LiteralPath)
    if (-not (Test-Path -LiteralPath $LiteralPath)) { return }
    Write-Host "Ajustando permissoes da chave (requisito do OpenSSH no Windows)..."
    $who = "$($env:USERDOMAIN)\$($env:USERNAME)"
    $r = & icacls.exe $LiteralPath /inheritance:r 2>&1
    if ($LASTEXITCODE -ne 0) { throw "icacls /inheritance:r falhou: $r" }
    $r = & icacls.exe $LiteralPath /grant:r "${who}:(R)" 2>&1
    if ($LASTEXITCODE -ne 0) { throw "icacls /grant falhou: $r" }
}

function Assert-OpenSshPrivateKeyReadable {
    param([Parameter(Mandatory)][string]$LiteralPath)
    $null = & ssh-keygen.exe -y -f $LiteralPath 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw @"
Chave privada invalida ou corrompida (ssh-keygen nao leu o arquivo): $LiteralPath

Causas comuns: arquivo truncado, trecho duplicado ao colar, arquivo errado (ex.: so a publica), salvo em UTF-16.

Corrija assim:
  - Restaure o .key original do ssh-keygen, ou gere outro par:
    ssh-keygen -t ed25519 -f `"$env:USERPROFILE\.ssh\id_ed25519_automais`"
  - Instale a PUBLICA no servidor:
    Get-Content `"$env:USERPROFILE\.ssh\id_ed25519_automais.pub`" | ssh root@SEU_HOST `"mkdir -p ~/.ssh && cat >> ~/.ssh/authorized_keys`"
  - Aponte KEY= no credenciaisssh.env para o arquivo privado correto.
"@
    }
}

$RemoteHost = Get-Cred 'HOST'
$RemoteUser = Get-Cred 'USER'
$IdentityFile = Get-Cred 'KEY'

if (-not $RemoteHost -or -not $RemoteUser) {
    throw "credenciaisssh.env precisa definir HOST= e USER=."
}
if (-not $IdentityFile) {
    throw "credenciaisssh.env precisa definir KEY= (caminho do arquivo .key ou PEM OpenSSH)."
}

$keyTmp = $null
if ($IdentityFile.StartsWith('-----BEGIN')) {
    $keyTmp = Join-Path $env:TEMP ("automais-env-deploy-{0}.key" -f [Guid]::NewGuid().ToString('N'))
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllText($keyTmp, $IdentityFile, $utf8)
    $who = "$($env:USERDOMAIN)\$($env:USERNAME)"
    $null = & icacls.exe $keyTmp /inheritance:r 2>$null
    $null = & icacls.exe $keyTmp /grant:r "${who}:(R)" 2>$null
    $IdentityFile = $keyTmp
}
else {
    if (-not (Test-Path -LiteralPath $IdentityFile)) {
        throw "Chave SSH nao encontrada: $IdentityFile"
    }
    $IdentityFile = (Resolve-Path -LiteralPath $IdentityFile).Path
    Repair-OpenSshPrivateKeyAcl -LiteralPath $IdentityFile
}

Assert-OpenSshPrivateKeyReadable -LiteralPath $IdentityFile

$StagingPath = '/tmp/automais.io.deploy.env'
$RemotePath = '/root/automais.io/automais.io.env'
$localResolved = (Resolve-Path -LiteralPath $LocalEnvFile).Path

$remoteBash = @'
set -e
R='/root/automais.io/automais.io.env'
S='/tmp/automais.io.deploy.env'
B=/tmp/automais.io.deploy.bak.$$
if sudo test -f "$R"; then sudo cp "$R" "$B"; fi
sudo cp "$S" "$R"
if ! sudo test -s "$R"; then
  if sudo test -f "$B"; then sudo cp "$B" "$R"; fi
  sudo rm -f "$S" "$B"
  echo "ERRO: destino vazio apos copia; backup restaurado se existia." >&2
  exit 1
fi
sudo chmod 600 "$R"
sudo rm -f "$S" "$B"
# Units com EnvironmentFile=-/root/automais.io/automais.io.env (ver deploy/*.service no repo)
UNITS=(
  hosts.service
  remote.service
  vpnserverio.service
  routeros.service
  webdevice.service
  automais-manager.service
  automais-api.service
)
echo "systemctl restart ${UNITS[*]}..."
sudo systemctl restart "${UNITS[@]}"
echo "OK: automais.io.env publicado e servicos reiniciados."
'@
# bash no Linux falha com CRLF (set: invalid option); normalizar para LF.
$remoteBash = ($remoteBash -replace "`r`n", "`n").Replace("`r", "`n")

$sshTarget = "${RemoteUser}@${RemoteHost}"
$sshArgs = @('-i', $IdentityFile)

try {
    Write-Host "scp -> $sshTarget : $StagingPath"
    Write-Host "  (local) $localResolved -> (remoto) $RemotePath"
    & scp @sshArgs $localResolved "${sshTarget}:$StagingPath"
    if ($LASTEXITCODE -ne 0) { throw "scp falhou (codigo $LASTEXITCODE)." }

    Write-Host "ssh: backup + copia + chmod 600 + systemctl restart (7 units)..."
    $remoteBash | & ssh @sshArgs $sshTarget bash -s
    if ($LASTEXITCODE -ne 0) { throw "Comandos remotos falharam (codigo $LASTEXITCODE)." }

    Write-Host "Concluido."
}
finally {
    if ($keyTmp -and (Test-Path -LiteralPath $keyTmp)) {
        Remove-Item -LiteralPath $keyTmp -Force -ErrorAction SilentlyContinue
    }
}
