Update package files stored in this source folder:

- AgentServices.exe
- AgentUpdater.exe

When running from Visual Studio, this folder is copied to:
AgentControl\bin\Debug\net8.0-windows\Updates\AgentServices

Debug, Release, and publish builds copy these files automatically beside
AgentControl.exe under Updates\AgentServices.

After changing AgentServices or AgentUpdater, publish both projects as
self-contained win-x64 single-file executables and replace the two EXE files here.

Current package version: 1.10
AgentServices.exe SHA-256: 35AA47BB5160949469F66D3FC8DBF9E6A6930F2FFAA42E2EBC89E3F91E363610
AgentUpdater.exe SHA-256: E6AECEB17C5746CED91E5D58725446401281DF477169CA35AF0ABAE8043AC6AD
