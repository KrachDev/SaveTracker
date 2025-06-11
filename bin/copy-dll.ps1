$source = "C:\Users\Krach\Documents\SaveTracker\SaveTracker\bin\Debug\SaveTracker.dll"
$destination = "C:\Users\Krach\AppData\Local\Playnite\Extensions\SaveTracker\SaveTracker.dll"

Copy-Item -Path $source -Destination $destination -Force
Write-Host "Copied SaveTracker.dll to Playnite extensions folder."
