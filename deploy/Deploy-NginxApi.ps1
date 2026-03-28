#Requires -Version 5.1
<#
.SYNOPSIS
  Publica nginx-api.automais.io.conf e nginx-automais.io.conf em sites-available no servidor.

.DESCRIPTION
  Credenciais em docs\credenciaisssh.env (gitignored):
    HOST - hostname ou IP
    USER - usuário SSH
    KEY  - caminho absoluto do arquivo .key (pode conter espacos) ou chave PEM multilinha (BEGIN...END)

  Mapeamento (nome no repo ≠ nome no servidor — ver $nginxPublish no script):
    nginx-api.automais.io.conf  -> /etc/nginx/sites-available/automais-api
    nginx-automais.io.conf      -> /etc/nginx/sites-available/automais.io

  Fluxo: scp para /tmp, backup, copia, sudo nginx -t, reload; em falha restaura backups.
#>
param(
    [string]$CredentialsFile
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $here '..\..')).Path

if (-not $CredentialsFile) {
    $CredentialsFile = Join-Path $repoRoot 'docs\credenciaisssh.env'
}
if (-not (Test-Path $CredentialsFile)) {
    throw "Arquivo de credenciais nao encontrado: $CredentialsFile"
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

# OpenSSH no Windows recusa chave se BUILTIN\Usuarios (ou outros) tiverem acesso.
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

function Build-NginxRemoteDeployBash {
    param([Parameter(Mandatory)][hashtable[]]$Sites)
    $n = $Sites.Count
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add('set -e')
    for ($i = 0; $i -lt $n; $i++) {
        $rEsc = $Sites[$i].RemotePath -replace "'", "'\''"
        $sEsc = $Sites[$i].StagingPath -replace "'", "'\''"
        [void]$lines.Add(("R{0}='{1}'" -f $i, $rEsc))
        [void]$lines.Add(("S{0}='{1}'" -f $i, $sEsc))
        [void]$lines.Add(('B{0}=/tmp/nginx.automais.deploy.bak.$$.{0}' -f $i))
    }
    for ($i = 0; $i -lt $n; $i++) {
        [void]$lines.Add(('if sudo test -f "$R{0}"; then sudo cp "$R{0}" "$B{0}"; fi' -f $i))
    }
    for ($i = 0; $i -lt $n; $i++) {
        [void]$lines.Add(('sudo cp "$S{0}" "$R{0}"' -f $i))
    }
    [void]$lines.Add('if ! sudo nginx -t; then')
    for ($i = 0; $i -lt $n; $i++) {
        [void]$lines.Add(('  if sudo test -f "$B{0}"; then sudo cp "$B{0}" "$R{0}"; fi' -f $i))
    }
    $sOnly = (0..($n - 1) | ForEach-Object { ('"$S{0}"' -f $_) }) -join ' '
    [void]$lines.Add("  sudo rm -f $sOnly")
    [void]$lines.Add('  exit 1')
    [void]$lines.Add('fi')
    [void]$lines.Add('sudo systemctl reload nginx')
    $allRm = (0..($n - 1) | ForEach-Object { ('"$S{0}" "$B{0}"' -f $_) }) -join ' '
    [void]$lines.Add("sudo rm -f $allRm")
    [void]$lines.Add("echo 'OK: nginx -t passou e reload concluido.'")
    ($lines -join "`n")
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
    $keyTmp = Join-Path $env:TEMP ("automais-nginx-deploy-{0}.key" -f [Guid]::NewGuid().ToString('N'))
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

# Nome do .conf no repositório ≠ nome do ficheiro em sites-available no Linux (única fonte do mapeamento).
$nginxPublish = @(
    @{
        LocalConf   = 'nginx-api.automais.io.conf'
        RemotePath  = '/etc/nginx/sites-available/automais-api'
        StagingPath = '/tmp/nginx-api.automais.deploy.conf'
    },
    @{
        LocalConf   = 'nginx-automais.io.conf'
        RemotePath  = '/etc/nginx/sites-available/automais.io'
        StagingPath = '/tmp/nginx-automais.io.deploy.conf'
    }
)
foreach ($site in $nginxPublish) {
    $lp = Join-Path $here $site.LocalConf
    if (-not (Test-Path -LiteralPath $lp)) { throw "Arquivo nao encontrado: $lp" }
}

$sshTarget = "${RemoteUser}@${RemoteHost}"
$sshArgs = @('-i', $IdentityFile)
$remoteBash = Build-NginxRemoteDeployBash -Sites $nginxPublish

try {
    Write-Host "scp -> $sshTarget ($($nginxPublish.Count) arquivos; repo -> sites-available):"
    foreach ($site in $nginxPublish) {
        $localPath = Join-Path $here $site.LocalConf
        Write-Host "  $($site.LocalConf) -> $($site.RemotePath)"
        & scp @sshArgs $localPath "${sshTarget}:$($site.StagingPath)"
        if ($LASTEXITCODE -ne 0) { throw "scp falhou: $($site.LocalConf) (codigo $LASTEXITCODE)." }
    }

    Write-Host "ssh: nginx -t + reload..."
    $remoteBash | & ssh @sshArgs $sshTarget bash -s
    if ($LASTEXITCODE -ne 0) { throw "Comandos remotos falharam (codigo $LASTEXITCODE)." }

    Write-Host "Concluido."
}
finally {
    if ($keyTmp -and (Test-Path -LiteralPath $keyTmp)) {
        Remove-Item -LiteralPath $keyTmp -Force -ErrorAction SilentlyContinue
    }
}
