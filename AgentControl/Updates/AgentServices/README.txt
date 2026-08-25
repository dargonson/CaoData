Update package files stored in this source folder:

- AgentServices.exe
- AgentUpdater.exe

When running from Visual Studio, this folder is copied to:
AgentControl\bin\Debug\net8.0-windows\Updates\AgentServices

Debug, Release, and publish builds copy these files automatically beside
AgentControl.exe under Updates\AgentServices.

After changing AgentServices or AgentUpdater, publish both projects as
self-contained win-x64 single-file executables and replace the two EXE files here.
