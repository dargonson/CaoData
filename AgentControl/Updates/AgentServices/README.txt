Update package files stored in this source folder:

- AgentServices.exe
- AgentUpdater.exe

When running from Visual Studio, this folder is copied to:
AgentControl\bin\Debug\net8.0-windows\Updates\AgentServices

Debug, Release, and publish builds copy these files automatically beside
AgentControl.exe under Updates\AgentServices.

After changing AgentServices or AgentUpdater, publish both projects as
self-contained win-x64 single-file executables and replace the two EXE files here.

Current package version: 1.9
AgentServices.exe SHA-256: 1C73DEBFCBFF39A900B8FE43484ED4A0904827C3AB6ADD55F709381FF9378C0B
AgentUpdater.exe SHA-256: 1AB8CEFC541D8CF7F5F4E69C0FF5A976E4BEC8923264920E2DA525C8B7853F35
