param(
    [string]$Exe = "C:\Users\cattu\Documents\Directories\Denuvo\RHI-CLI\rhictl.exe",
    [string]$Game = "Death in the Water 2"
)

$ErrorActionPreference = "Continue"
$results = New-Object System.Collections.Generic.List[object]

$script:Installed = @{}
try {
    $statusProbe = & $Exe status --game $Game --json 2>$null | Out-String | ConvertFrom-Json
    foreach ($c in $statusProbe.data.components) { $script:Installed[$c.key] = [bool]$c.installed }
} catch { }

function Run-Check {
    param(
        [string]$Name,
        [string[]]$Arguments,
        [int[]]$Expected = @(0),
        [string]$ErrorCode = "",
        [switch]$Json,
        [string]$SkipUnlessComponent = ""
    )

    if ($SkipUnlessComponent -and -not $script:Installed[$SkipUnlessComponent]) {
        $results.Add([pscustomobject]@{ Check = $Name; Exit = 0; Pass = $true; Detail = "skipped ($SkipUnlessComponent not installed)" })
        return
    }

    $out = & $Exe @Arguments 2>&1 | Out-String
    $code = $LASTEXITCODE
    $pass = $Expected -contains $code
    $detail = ""

    if ($Json -and $pass) {
        try {
            $parsed = $out | ConvertFrom-Json
            if ($parsed.ok -ne $true) { $pass = $false; $detail = "ok != true" }
            else { $detail = "json ok" }
        } catch {
            $pass = $false
            $detail = "invalid json"
        }
    }

    if (-not $pass -and $ErrorCode) {
        try {
            $parsed = $out | ConvertFrom-Json
            if ($parsed.error.code -eq $ErrorCode) { $pass = $true; $detail = "expected error: $ErrorCode" }
        } catch { }
    }

    $results.Add([pscustomobject]@{ Check = $Name; Exit = $code; Pass = $pass; Detail = $detail })
    if (-not $pass) {
        Write-Host "FAIL: $Name (exit $code) $detail"
        Write-Host $out
    }
}

Run-Check "doctor"            @("doctor","--json") -Json
Run-Check "paths"             @("paths","--json") -Json
Run-Check "library list"      @("library","list","--json") -Json
Run-Check "status"            @("status","--game",$Game,"--json") -Json
Run-Check "dlss status"       @("dlss","status","--game",$Game,"--json") -Json
Run-Check "dlss scales"       @("dlss","scales","--json") -Json
Run-Check "nvidia info"       @("nvidia","info","--json") -Json
Run-Check "nvidia status"     @("nvidia","status","--game",$Game,"--json") -Json
Run-Check "hdr status"        @("hdr","status","--json") -Json
Run-Check "res current"       @("res","current","--json") -Json
Run-Check "vulkan status"     @("vulkan","status","--json") -Json
Run-Check "shaders list"      @("shaders","list","--json") -Json
Run-Check "addons list"       @("addons","list","--json") -Json
Run-Check "preset list"       @("preset","list","--json") -Json
Run-Check "config list"       @("config","list","--json") -Json
Run-Check "config set dry"    @("config","set","--key","CliSelfTest","--value","1","--dry-run","--json") -Json
Run-Check "reset-overrides dry" @("game","reset-overrides","--game",$Game,"--dry-run","--json") -Json
Run-Check "set-launch dry"    @("game","set-launch","--game",$Game,"--args","-windowed","--dry-run","--json") -Json
Run-Check "set-name dry"      @("game","set-name","--game",$Game,"--name","Cli Rename Test","--dry-run","--json") -Json
Run-Check "set-dll dry"       @("game","set-dll","--game",$Game,"--reshade","dxgi.dll","--dry-run","--json") -Json
Run-Check "reset-dll dry"     @("game","reset-dll","--game",$Game,"--dry-run","--json") -Json
Run-Check "set-override dry"  @("game","set-override","--game",$Game,"--kind","channel","--value","nightly","--dry-run","--json") -Json
Run-Check "clear-override dry" @("game","clear-override","--game",$Game,"--kind","channel","--dry-run","--json") -Json
Run-Check "renodx ini-get"    @("renodx","ini-get","--game",$Game,"--section","renodx-preset1","--key","toneMapPeakNits","--json") -Json -SkipUnlessComponent "renodx"
Run-Check "relimiter ini-list" @("relimiter","ini-list","--game",$Game,"--json") -Json -SkipUnlessComponent "relimiter"

Run-Check "reshade install dry"        @("reshade","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "renodx install dry"         @("renodx","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "relimiter install dry"      @("relimiter","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "display-commander dry"      @("display-commander","install","--game",$Game,"--dry-run","--json") -Expected @(0,2) -ErrorCode "CONFLICT"
Run-Check "asi install dry"            @("asi","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "dlss-enabler dry"           @("dlss-enabler","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "fg2030 dry"                 @("fg2030","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "rtx-encore dry"             @("rtx-encore","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "mfg40 dry"                  @("mfg40","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "dgvoodoo dry"               @("dgvoodoo","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "dxvk install dry"           @("dxvk","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "optiscaler install dry"     @("optiscaler","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "reframework install dry"    @("reframework","install","--game",$Game,"--dry-run","--json") -Json
Run-Check "dofix install dry"          @("dofix","install","--game",$Game,"--dry-run","--json") -Expected @(0,2) -ErrorCode "NOT_ELIGIBLE"
Run-Check "preset deploy dry"          @("preset","deploy","--game",$Game,"--dry-run","--json") -Expected @(0,3) -ErrorCode "NO_PRESETS"
Run-Check "renodx mode dry"            @("renodx","mode","--game",$Game,"--value","intermediate","--dry-run","--json") -Json
Run-Check "renodx peak-nits dry"       @("renodx","peak-nits","--game",$Game,"--value","800","--dry-run","--json") -Json
Run-Check "update all dry"             @("update","all","--components","reshade","--game",$Game,"--dry-run","--json") -Json
Run-Check "library refresh dry"        @("library","refresh","--dry-run","--json") -Json

Run-Check "no-yes guard" @("reshade","install","--game",$Game) -Expected @(2)
Run-Check "vulkan install elevation" @("vulkan","install","--layer-only","--yes") -Expected @(5)

$payload = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}',
    '{"jsonrpc":"2.0","method":"notifications/initialized"}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list"}',
    '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"rhi_status","arguments":{"game":"' + $Game + '"}}}',
    '{"jsonrpc":"2.0","id":4,"method":"shutdown"}'
) -join "`n"
$mcpOut = $payload | & $Exe mcp serve 2>&1 | Out-String
$mcpPass = ($mcpOut -match '"result"') -and ($mcpOut -match 'rhi_status')
$results.Add([pscustomobject]@{ Check = "mcp handshake"; Exit = 0; Pass = $mcpPass; Detail = "" })
if (-not $mcpPass) { Write-Host "FAIL: mcp handshake"; Write-Host $mcpOut }

$results | Format-Table -AutoSize | Out-String -Width 120
$failed = ($results | Where-Object { -not $_.Pass }).Count
"TOTAL: $($results.Count)  PASS: $($results.Count - $failed)  FAIL: $failed"
exit $(if ($failed -eq 0) { 0 } else { 1 })
