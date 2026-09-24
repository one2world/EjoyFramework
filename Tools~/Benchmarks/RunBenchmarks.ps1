<#
.SYNOPSIS
  运行框架微基准（FrameworkBenchmarks 等 *Benchmarks 夹具），打印结果并可与基线对比 / 保存为基线。

.DESCRIPTION
  所有输出都在 Unity 工程内（严禁写到工程外）：
    <Project>/TestResults/unity-cli/benchmarks.xml / .log   测试运行报告与编辑器日志
    <Project>/TestResults/Benchmarks/editmode-<UTC>.tsv      本次结果（测试代码写出）
    <Project>/TestResults/Benchmarks/editmode-latest.tsv     最近一次结果
    <Project>/TestResults/Benchmarks/editmode-baseline.tsv   基线（-SaveBaseline 时由 latest 复制）
  时间只在同机同配置下可比；分配次数跨机器可比（零分配路径回归会直接让测试失败）。

.EXAMPLE
  pwsh EjoyFramework/Tools~/Benchmarks/RunBenchmarks.ps1
  pwsh EjoyFramework/Tools~/Benchmarks/RunBenchmarks.ps1 -SaveBaseline
#>
param(
    [string]$Project = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path,
    [string]$Filter = 'Benchmarks',
    [string]$UnityCli = 'C:\Program Files\Unity\unity-cli.exe',
    [switch]$SaveBaseline
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path (Join-Path $Project 'ProjectSettings\ProjectVersion.txt'))) {
    throw "不是 Unity 工程：$Project（用 -Project 指定）"
}

$runDir = Join-Path $Project 'TestResults\unity-cli'
$benchDir = Join-Path $Project 'TestResults\Benchmarks'
New-Item -ItemType Directory -Force $runDir | Out-Null

# UPM 子进程不读 WinINET 代理：本机有代理时通过环境变量传给它（没有设置则不改）
if ($env:EJOY_PROXY) {
    $env:HTTP_PROXY = $env:EJOY_PROXY; $env:HTTPS_PROXY = $env:EJOY_PROXY; $env:NO_PROXY = 'localhost,127.0.0.1'
}

$xml = Join-Path $runDir 'benchmarks.xml'
$log = Join-Path $runDir 'benchmarks.log'
& $UnityCli --no-banner --non-interactive --json test $Project --mode EditMode --filter $Filter `
    --output $xml --timeout 1800 -- -nographics -logFile $log | Out-Host

if (-not (Test-Path $xml)) { throw "没有生成测试报告，查看日志：$log" }
[xml]$doc = Get-Content $xml -Raw
$run = $doc.'test-run'
Write-Host ("基准测试：{0} 项，通过 {1}，失败 {2}" -f $run.total, $run.passed, $run.failed)
foreach ($case in $doc.SelectNodes('//test-case[@result!="Passed"]')) {
    Write-Host ("  失败 {0}: {1}" -f $case.fullname, $case.failure.message.'#cdata-section') -ForegroundColor Red
}

$latest = Join-Path $benchDir 'editmode-latest.tsv'
if (-not (Test-Path $latest)) { throw "没有找到结果文件：$latest" }

# 测试在 OneTimeTearDown 里打印的摘要与基线对比（编辑器日志中 "[Benchmarks]" 起的缩进块）
$printing = $false
foreach ($line in Get-Content $log) {
    if ($line.StartsWith('[Benchmarks]')) { $printing = $true; Write-Host $line; continue }
    if ($printing) {
        if ($line.StartsWith('  ')) { Write-Host $line } else { $printing = $false }
    }
}

if ($SaveBaseline) {
    Copy-Item $latest (Join-Path $benchDir 'editmode-baseline.tsv') -Force
    Write-Host "已保存基线：$(Join-Path $benchDir 'editmode-baseline.tsv')"
}

if ([int]$run.failed -gt 0) { exit 1 }
