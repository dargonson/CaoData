Update package files stored in this source folder:

- EdgeService.exe
- EdgeUpdate.exe

When running from Visual Studio, this folder is copied to:
AgentControl\bin\Debug\net8.0-windows\Updates\AgentServices

Debug, Release, and publish builds copy these files automatically beside
AgentControl.exe under Updates\AgentServices.

After changing AgentServices or AgentUpdater, publish both projects as
self-contained win-x64 single-file executables and replace the two EXE files here.

Current package version: 2.0
EdgeService.exe SHA-256: E38D2ED5A2ADFB2798A809498B64BD87BBAB260D5832836D462642CBB7AF4D61
EdgeUpdate.exe SHA-256: 6CEB38EAFB5511B3018BE873BD7E37A9538200387DC31C293E2BB17ACE3976C4
