# Makes THIS computer trust the test signing certificate of the releases (Smart Point, test signing).
# Run it once, as administrator, on a computer where you test: after that the installer and the app show the publisher
# "Smart Point (test signing)" and Windows does not call it an unknown file.
#
# This is for your own test computers only. A certificate that you trust yourself means nothing to any other computer; the
# warning for everybody else goes away only with a certificate from a certificate authority (see docs/RELEASING.md).
#
#   Right-click PowerShell -> Run as administrator, then:
#   .\trust-test-certificate.ps1
param([string]$Certificate = (Join-Path $PSScriptRoot "SmartPoint-test-signing.cer"))

$ErrorActionPreference = "Stop"

$admin = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "Run this script as administrator (right-click PowerShell, Run as administrator)." }
if (-not (Test-Path $Certificate)) { throw "The certificate file was not found: $Certificate" }

$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Resolve-Path $Certificate))
Write-Host "Certificate: $($cert.Subject)"
Write-Host "Thumbprint:  $($cert.Thumbprint)"
Write-Host "Valid until: $($cert.NotAfter.ToString('yyyy-MM-dd'))"
Write-Host ""

foreach ($store in "Root", "TrustedPublisher") {
    Import-Certificate -FilePath $Certificate -CertStoreLocation "Cert:\LocalMachine\$store" | Out-Null
    Write-Host "Added to the trusted store: $store" -ForegroundColor Green
}

Write-Host ""
Write-Host "Done. To undo: certmgr.msc -> Trusted Root Certification Authorities and Trusted Publishers -> remove 'Smart Point (test signing)'."
