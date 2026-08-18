# Building the installer

Requires Inno Setup 6 (`choco install innosetup -y`, needs an elevated shell).

```
powershell -ExecutionPolicy Bypass -File installer\publish.ps1
"C:\ProgramData\chocolatey\bin\ISCC.exe" installer\CartridgeOS.iss
```

Output: `installer\output\CartridgeOS-Setup.exe`. Requires admin to run (installs to
`Program Files`, registers the `CartridgeOS` Windows Service).
