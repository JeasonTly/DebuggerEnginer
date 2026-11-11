$errs = $null
$tokens = $null
[void][System.Management.Automation.Language.Parser]::ParseFile(
  'E:\CCode\TestEngineer\scripts\selftest-dual-full.ps1',
  [ref]$tokens,
  [ref]$errs)
if ($errs -and $errs.Count -gt 0) {
  foreach ($e in $errs) { Write-Output $e.ToString() }
  exit 1
}
Write-Output 'PARSE_OK'
exit 0
