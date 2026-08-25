@echo off
REM Build FINDER from source using the C# compiler bundled with Windows (.NET Framework).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REF=%ProgramFiles(x86)%\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8

"%CSC%" /nologo /optimize+ /target:winexe /win32icon:finder.ico /resource:finder.png,finder.png /out:FinderApp.exe ^
  /r:"%REF%\PresentationFramework.dll" ^
  /r:"%REF%\PresentationCore.dll" ^
  /r:"%REF%\WindowsBase.dll" ^
  /r:"%REF%\System.Xaml.dll" ^
  /r:System.dll ^
  FinderApp.cs

if exist FinderApp.exe (echo Build OK: FinderApp.exe) else (echo Build FAILED)
endlocal
