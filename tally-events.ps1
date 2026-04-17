param([string]$LogPath)
Get-Content $LogPath | ForEach-Object {
    if ($_ -match '"EventType":"([a-z._]+)"') { $Matches[1] }
} | Group-Object | Sort-Object Count -Descending | Select-Object Count,Name | Format-Table -AutoSize
