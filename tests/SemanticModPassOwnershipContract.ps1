param()
$ErrorActionPreference='Stop'
$root=Join-Path (Get-Location) 'src/G.CETProfiler.Core/Services'
$main=Join-Path $root 'SemanticInjectors.cs'
$modFolder=Join-Path $root 'ModPasses'
if(!(Test-Path $main) -or !(Test-Path $modFolder)){throw 'Semantic source structure missing'}
$dispatch=Get-Content $main -Raw
if($dispatch -notmatch 'internal static partial class SemanticInjectors'){throw 'Dispatcher is not a partial class'}
if($dispatch -match '(?m)^\s*private static SemanticInjectionResult Apply[A-Za-z0-9_]+\('){
  throw 'Mod-specific semantic injector migrated back into shared dispatcher'
}
$files=@(Get-ChildItem $modFolder -Filter '*.cs' -File)
$methods=@{}
$count=0
foreach($file in $files){
  $source=Get-Content $file.FullName -Raw
  if($source -notmatch 'internal static partial class SemanticInjectors'){throw "Not a shared partial class: $($file.Name)"}
  $decls=@([regex]::Matches($source,'(?m)^\s*private static SemanticInjectionResult (Apply[A-Za-z0-9_]+)\('))
  if($decls.Count -eq 0){throw "Empty mod injector file $($file.Name)"}
  foreach($m in $decls){
    $name=$m.Groups[1].Value
    if($methods.ContainsKey($name)){throw "Duplicate injector: $name"}
    $methods[$name]=$file.Name
    $count++
  }
}
$mapped=@([regex]::Matches($dispatch,'=>\s*(Apply[A-Za-z0-9_]+)\s*\(context\)'))
foreach($match in $mapped){
  if(!$methods.ContainsKey($match.Groups[1].Value)){
    throw "Dispatcher references an unowned injector: $($match.Groups[1].Value)"
  }
}
if($count -lt 64){throw "Missing previously consolidated injector methods: $count"}
foreach($pair in @(@('ApplyAutoLoot','ApplyAutoLootWorldPinAndEarlyDistance','ApplyAutoLootWorldPinAllocationScan'),@('ApplyEasyTrainer','ApplyEasyTrainerBackgroundDormancy'),@('ApplyGoodFeelings','ApplyGoodFeelingsHardDraw'),@('ApplyMinimapWidgets','ApplyMinimapStealthWidgetLocality'))){
 $owners=@($pair|ForEach-Object {$methods[$_] }|Sort-Object -Unique)
 if($owners.Count -ne 1 -or !$owners[0]){throw "Related mod source stages split across separate files: $($pair -join ',')"}
}
Write-Host "Mod-pass source ownership passed: $count methods in $($files.Count) mod-owned files, centralized dispatch and shared helpers."
