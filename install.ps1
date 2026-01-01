Invoke-WebRequest -Uri "https://github.com/bugtesterdani/C--Installer-without-Admin/releases/download/v1.0.0/publisher.cer" -OutFile "publisher.cer"
Invoke-WebRequest -Uri "https://github.com/bugtesterdani/C--Installer-without-Admin/releases/download/v1.0.5.0/MSIX.Installer_1.0.5.0_x86_x64_ARM64.msixbundle" -OutFile "MSIX.Installer_1.0.5.0_x86_x64_ARM64.msixbundle"
Import-Certificate -FilePath .\publisher.cer -CertStoreLocation Cert:\LocalMachine\Root
Add-AppxProvisionedPackage -Online -PackagePath .\MSIX.Installer_1.0.5.0_x86_x64_ARM64.msixbundle -SkipLicense
