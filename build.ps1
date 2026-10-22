$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (!(Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework/v4.0.30319/csc.exe' }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$src = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $build | Out-Null
$baseArgs = @('/nologo','/target:winexe','/platform:anycpu','/optimize+','/utf8output',('/win32icon:' + (Join-Path $assets 'mojo.ico')),('/win32manifest:' + (Join-Path $src 'app.manifest')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Runtime.Serialization.dll','/reference:System.Xml.dll')
$motionArgs = @()
$motionRoot = Join-Path $assets 'motion'
if (Test-Path -LiteralPath $motionRoot) {
    foreach ($page in Get-ChildItem -LiteralPath $motionRoot -Filter '*.png' | Sort-Object Name) {
        $motionArgs += '/resource:' + $page.FullName + ',motion.' + $page.Name
    }
    $motionArgs += '/resource:' + (Join-Path $motionRoot 'animations.json') + ',animations.json'
}
$petSources = @('Pet.cs','AnimationModel.cs','FrameCache.cs','AnimationEngine.cs','PetForm.cs','MotionTests.cs','NativeSmoke.cs') | ForEach-Object { Join-Path $src $_ }
& $compiler @baseArgs @motionArgs ('/out:' + (Join-Path $build 'MojoCarrot.exe')) ('/resource:' + (Join-Path $assets 'sprites.png') + ',sprites.png') ('/resource:' + (Join-Path $assets 'mojo.ico') + ',mojo.ico') @petSources
if ($LASTEXITCODE -ne 0) { throw 'Pet compile failed' }
& $compiler @baseArgs ('/out:' + (Join-Path $build 'Uninstall.exe')) (Join-Path $src 'InstallCommon.cs') (Join-Path $src 'Uninstall.cs')
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compile failed' }
& $compiler @baseArgs ('/out:' + (Join-Path $build 'MojoCarrot-Setup-1.2.0.exe')) ('/resource:' + (Join-Path $build 'MojoCarrot.exe') + ',MojoCarrot.exe') ('/resource:' + (Join-Path $build 'Uninstall.exe') + ',Uninstall.exe') ('/resource:' + (Join-Path $assets '使用说明.txt') + ',使用说明.txt') ('/resource:' + (Join-Path $assets 'mojo.ico') + ',mojo.ico') (Join-Path $src 'InstallCommon.cs') (Join-Path $src 'Setup.cs')
if ($LASTEXITCODE -ne 0) { throw 'Setup compile failed' }
Get-ChildItem -LiteralPath $build -Filter '*.exe' | Select-Object Name,Length
