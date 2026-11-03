# Signs files (Authenticode, SHA-256, with a timestamp) when a certificate is given in the environment; otherwise leaves them as they are.
#
#   SIGNING_PFX_BASE64    the .pfx file, base64 encoded (a GitHub secret in the release workflow)
#   SIGNING_PFX_PASSWORD  its password
#
#   .\installer\sign.ps1 path\to\CompanyTimesheet.exe path\to\installer.msi
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Path)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrEmpty($env:SIGNING_PFX_BASE64)) {
    Write-Host "No signing certificate in the environment: the files stay unsigned." -ForegroundColor Yellow
    return
}

$pfx = Join-Path ([IO.Path]::GetTempPath()) ("sign-" + [guid]::NewGuid().ToString("N") + ".pfx")
[IO.File]::WriteAllBytes($pfx, [Convert]::FromBase64String($env:SIGNING_PFX_BASE64))
try {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfx, $env:SIGNING_PFX_PASSWORD)
    foreach ($file in $Path) {
        $result = Set-AuthenticodeSignature -FilePath $file -Certificate $certificate -HashAlgorithm SHA256 -TimestampServer "http://timestamp.digicert.com"
        # A self-signed certificate is "not trusted" on the machine that signs; what matters here is that a signature was written
        if ($null -eq $result.SignerCertificate -or $result.Status -in "HashMismatch", "NotSigned") { throw "Signing $file failed: $($result.StatusMessage)" }
        Write-Host "Signed $(Split-Path -Leaf $file) as $($certificate.Subject)" -ForegroundColor Green
    }
}
finally {
    Remove-Item $pfx -Force -ErrorAction SilentlyContinue
}
