param([Parameter(Mandatory=$true)][string]$ResolverRoot)
$ErrorActionPreference='Stop'
$root=Join-Path $env:RUNNER_TEMP 'gcet-marmur-native-worker'
$folder=Join-Path $root 'Marmur Bank'
if(Test-Path $root){Remove-Item -Recurse -Force $root}
New-Item -ItemType Directory -Force (Join-Path $folder 'module')|Out-Null
$source=@'
function bank:registerSpatialSet()
  if not self.engine or self.__gcetNativeFar then return end
  if not self.__gcetNativeAttached then
    local lookup=self.engine.GCET and self.engine.GCET.NativeLocationHost
    if lookup and type(lookup.attachCompiled)=="function" then
      self.__gcetNativeAttached=lookup:attachCompiled(10036,#PosData.All,
        function()
          self.__gcetNativeFar=false
          self:registerSpatialSet()
        end,
        function()
          self.__gcetNativeFar=true
          self.nearestLocation=nil
          if self.spatialHandle then
            self.spatialHandle:unregister()
            self.spatialHandle=nil
          end
        end,
        function()
          self.__gcetNativeFar=false
          self:registerSpatialSet()
          return true
        end)==true
    end
  end
end
function bank:distanceListener()
  if self.__gcetNativeFar then self.nearestLocation=nil;return 0 end
  return self.engine.QueryNearest("marmurbank")
end
'@
$asm=[Reflection.Assembly]::LoadFrom((Join-Path (Resolve-Path $ResolverRoot).Path 'G.CETProfiler.Core.dll'))
$inject=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticInjectors',$true)
$ctxType=$asm.GetType('GCETRuntimeProfiler.Core.Services.SemanticPatchContext',$true)
$flags=[Reflection.BindingFlags]'Static,NonPublic'
$ctor=@($ctxType.GetConstructors([Reflection.BindingFlags]'Instance,NonPublic')|Where-Object{$_.GetParameters().Count -eq 4})[0]
$m=$inject.GetMethod('ApplyMarmurBankNativeWorker',$flags)
if(!$m){throw 'Marmur Bank worker injector missing'}
function Apply([string]$sourceText){
 [IO.File]::WriteAllText((Join-Path $folder 'module/Bank.lua'),$sourceText,[Text.UTF8Encoding]::new($false))
 $ctx=$ctor.Invoke([object[]]@([string]$root,[string]'Marmur Bank',[string]'marmurbank-native-worker',[Collections.Generic.Dictionary[string,byte[]]]::new()))
 try{$result=$m.Invoke($null,[object[]]@($ctx))}catch{throw "Marmur worker generation failed: $($_.Exception.InnerException.Message)"}
 return @{result=$result;rows=@($ctxType.GetProperty('Changes',[Reflection.BindingFlags]'Instance,NonPublic').GetValue($ctx))}
}
$first=Apply $source
if(!$first.result.Applied -or $first.rows.Count -ne 2){throw 'Marmur expected bridge and worker outputs'}
$bridge=@($first.rows|Where-Object{$_.RelativeFile -like '*/module/Bank.lua'})|Select-Object -First 1
$worker=@($first.rows|Where-Object{$_.RelativeFile -like '*/GCET_Semantic/marmurbank_native_worker.lua'})|Select-Object -First 1
if(!$bridge -or !$worker){throw 'Marmur output missing'}
$b=[Text.Encoding]::UTF8.GetString($bridge.Bytes)
$w=[Text.Encoding]::UTF8.GetString($worker.Bytes)
foreach($x in @('GCET_Semantic/marmurbank_native_worker','self.__gcetNativeAttached','function bank:distanceListener()','QueryNearest("marmurbank")')){if(!$b.Contains($x)){throw "Marmur bridge omitted $x"}}
foreach($x in @('G-CET semantic coordinator:marmurbank-native-worker','host:attachCompiled(10036, count','bank.spatialHandle:unregister()','bank:registerSpatialSet()','bank.__gcetNativeFar = true')){if(!$w.Contains($x)){throw "Marmur worker omitted $x"}}
New-Item -ItemType Directory -Force (Join-Path $folder 'GCET_Semantic')|Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'GCET_Semantic/marmurbank_native_worker.lua'),$w,[Text.UTF8Encoding]::new($false))
$again=Apply $b
if($again.result.Applied){throw 'Marmur coordinator duplicated on repeat pass'}
Write-Host 'Marmur Bank native worker and idempotence: PASS'
