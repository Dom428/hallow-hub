# Hallow Hub uninstaller (run from Windows Settings > Apps, or directly).
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Hallow Hub'
Add-Type -AssemblyName System.Windows.Forms
$keep = [System.Windows.Forms.MessageBox]::Show("Uninstall Hallow Hub?`n`nYour games stay installed - only the launcher is removed.`n`nKeep your playtime and per-game choices in case you reinstall?", 'Uninstall Hallow Hub', 'YesNoCancel', 'Question')
if ($keep -eq 'Cancel') { return }
Get-Process HallowHub -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'HallowHub' -ErrorAction SilentlyContinue
foreach ($dir in [Environment]::GetFolderPath('Programs'), [Environment]::GetFolderPath('Desktop')) { Remove-Item (Join-Path $dir 'Hallow Hub.lnk') -ErrorAction SilentlyContinue }
Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\HallowHub' -Recurse -ErrorAction SilentlyContinue
if ($keep -eq 'No') { Remove-Item (Join-Path $env:APPDATA 'HallowHub') -Recurse -Force -ErrorAction SilentlyContinue }
# the folder holds this script, so remove it a moment after we exit
Start-Process cmd.exe -ArgumentList "/c timeout /t 2 /nobreak >nul & rmdir /s /q `"$dest`"" -WindowStyle Hidden
[System.Windows.Forms.MessageBox]::Show('Hallow Hub is uninstalled.', 'Hallow Hub') | Out-Null