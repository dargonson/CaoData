# PROJECT CONTEXT - Tool Backup / AgentControl / AgentServices

File nay dung de chuyen tiep sang cuoc tro chuyen Codex moi. Khi bat dau chat moi, hay bao Codex:

> Doc `PROJECT_CONTEXT.md`, kiem tra repo, roi tiep tuc lam viec theo context trong file nay.

## 1. Thong tin repo hien tai

- Workspace: `C:\Users\DoThai\Desktop\Repo#`
- Branch lam viec hien tai: `main`
- Remote branch hien tai: `origin/main`
- Trang thai Git sau lan don gan nhat:
  - Da hop nhat branch code moi nhat vao `main`.
  - Da xoa local branch code moi nhat.
  - Da xoa remote branch code moi nhat.
  - Da xoa branch `Complete-v1`.
  - Tu nay chi lam truc tiep tren `main`.
- Checkpoint truoc dot toi uu toan he thong: `e0527b4` - `TRUOC KHI TOI UU VA FIX TOAN BO`
- Tag checkpoint: `checkpoint-truoc-toi-uu-fix-20260825`
- Xem muc 17 de biet trang thai code/test moi nhat; khi chuyen may hay chay `git log -1 --oneline` de lay commit dau thuc te.
- Remote GitHub: `https://github.com/dargonson/CaoData`

## 2. Quy tac lam viec voi user

- User goi Codex la "fen", noi tieng Viet.
- User muon lam nhanh nhung rat so mat chuc nang da on dinh.
- Nguyen tac lon nhat: **khong duoc lam mat, tat bot, khoa lai, hay thay doi hanh vi cac chuc nang da chay on neu user khong yeu cau ro**.
- Khi sua code:
  - Doc code hien co truoc.
  - Sua dung vung lien quan.
  - Khong refactor lon neu khong bat buoc.
  - Khong doi UI/giao dien neu user khong yeu cau.
  - Khong thay doi kien truc neu khong that su can.
  - Khong xoa chuc nang cu de lam chuc nang moi.
- Khi build/test:
  - User da cho phep Codex build/test khi can.
  - Neu Visual Studio, `AgentControl.exe`, hoac app dang Start Debug va khoa file build thi duoc phep stop VS/app truoc khi build.
  - Stop dung tien trinh lien quan: `devenv`, `AgentControl`, neu can thi `AgentServices`.
  - Khong build vong qua output tam neu muc tieu la test chuan; hay build chuan vao `bin`.
- Khi thao tac Git:
  - Duoc phep xem status/log/diff, switch, merge, push theo yeu cau.
  - Khong `reset --hard` hay force push neu user khong yeu cau ro.
  - Neu user yeu cau don history/xoa commit/branch thi co the dung `reset --hard`, `branch -D`, `push --force-with-lease`, nhung phai kiem tra status truoc.

## 3. Cau truc project

Repo gom cac project chinh:

- `AgentControl`
  - WinForms app dieu khien trung tam.
  - Main form hien tai la class `frmToolBackup` trong `AgentControl/Form1.cs`.
  - UI gom:
    - List card Agent ben trai: custom `ListBoxNHF`.
    - TreeView/ListView hien thi o dia/thu muc/file remote.
    - `dgvDownloads` danh sach download.
    - `dvgUploads` danh sach upload.
    - Radio list: danh sach download/upload.
    - Checksum radio: SHA-256, MD5, None.
  - Dung SQLite de luu Agent, download queue, log, owner name...

- `AgentServices`
  - Windows Service chay tren may Agent.
  - Ket noi TCP ve AgentControl.
  - Lang nghe lenh tu Control: liet ke o dia/thu muc/file, download, upload, open, delete, update...
  - Chay duoc LAN va WAN.
  - Release dang self-contained single-file co nen.

- `AgentShared`
  - Shared models/protocol/version.
  - `AppVersion.cs` dang co:
    - `CurrentVersionControl = "1.8"`
    - `CurrentVersionAgent = "1.8"`
    - `AgentUpdateRootDirectory = @"C:\ProgramData\CaoData\AgentServices\Updates"`
    - update marker/log constants.

- `AgentUpdater`
  - EXE rieng dung cho auto update AgentServices.
  - AgentServices tai file update, goi AgentUpdater.
  - AgentUpdater stop service cu, copy de file moi, start service lai, gui status ve Control/log.
  - Release dang self-contained single-file co nen.

- `NHFUiControls/NHFUiControls`
  - Custom UI control, dac biet la `ListBoxNHF` ve card Agent.

## 4. Cac chuc nang quan trong da co va phai giu

### 4.1 Agent connection

- AgentServices ket noi ve AgentControl qua TCP, port co ban 9000 la nen tang quan trong.
- LAN da test OK.
- WAN da test OK sau khi cai runtime/self-contained va cau hinh ket noi.
- Agent card phai hien:
  - ComputerName
  - User
  - IP
  - OS
  - Agent ID
  - Online/Offline
  - Version Agent
  - Truong "Nguoi su dung" co the chinh sua.
- Khi Agent offline luc khoi dong thi phai hien Offline, khong duoc hien Online sai.
- Khi Agent version cu hon Control:
  - Khi click Agent de thao tac, phai canh bao co ban update moi.
  - Yes thi gui lenh update.
  - No thi khong thao tac tiep.

### 4.2 Remote browsing

- Click Agent se load danh sach o dia cua dung Agent do.
- Nhieu Agent ket noi cung luc phai tranh dung nham o dia/thu muc cua Agent khac.
- TreeView/ListView phai load dung folder/file theo Agent dang chon.
- Icon folder/file da tung bi loi voi nhieu Agent; khong duoc lam mat icon.
- Mo folder con trong ListView/TreeView da co.
- Mo file remote truc tiep da co, khong duoc khoa lai.

### 4.3 Download

- Download file tu Agent ve Control.
- Download folder da co va da test OK.
- Download nhieu file/folder da co.
- Resume download da co:
  - Neu Agent rot mang/tat may/shutdown/ngat ket noi, file chua xong phai ve Waiting/Waiting Agent va tiep tuc resume tu offset khi Agent ket noi lai.
  - Da fix loi resume file thu 2 bi Error.
- Download file lon da sua theo huong IDM:
  - Ghi stream/chunk xuong dia.
  - Khong giu file lon trong RAM.
  - Giam RAM tang cao.
- Duplicate local filename:
  - Neu file download bi trung ten tren o dia thi tu rename kieu browser: `file (1).ext`, `file (2).ext`.
  - Trong `dgvDownloads`, cot ten file phai hien ro neu doi ten: `ten.ext -- file bi trung, doi ten thanh ten (1).ext tren o dia`.
  - Khong hoi Yes/No/Cancel nua.
- `dgvDownloads` phai hien:
  - Ten file
  - Dung luong
  - Tien do progressbar va %
  - Toc do
  - Trang thai
- Khi download xong:
  - Cot tien do hien "Hoan Thanh" (bold, lon hon 1 size) thay vi progressbar.
  - Cot trang thai theo checksum mode:
    - None: `Done`
    - MD5: `[OK] MD5 Checksum: MATCHED`
    - SHA-256: `[OK] SHA-256 Checksum: MATCHED`
    - FAIL mau do.
- Error:
  - Progressbar 0%.
  - Fill processbar mau do.
  - Chu Error mau do, bold, co dau X do.
  - File loi phai bo qua va tiep tuc download cac file khac.
- Neu file tren Agent bi antivirus xoa giua chung:
  - Phai bao Error, khong duoc ket stuck 0% Downloading.

### 4.4 Checksum

- UI co group `grbchecksum` voi radio:
  - `radnone`
  - `radmd5`
  - `radsha256`
- None: khong check checksum.
- MD5: check MD5.
- SHA-256: check SHA-256.
- Download local Agent check OK.
- Da tung co loi download tu Agent khac checksum FAIL; can can than khi sua transmission/chunk/hash.
- User uu tien toc do, RAM/CPU nhe.

### 4.5 Upload

- Co chuc nang upload tu Control xuong Agent.
- Upload file da co.
- Upload folder da co.
- Da ho tro chon File hoac Folder trong mot luong Upload, khong can hoi Yes/No tach rieng.
- Da ho tro drag/drop file/folder vao `dvgUploads`.
- Neu `dvgUploads` trong:
  - Nut Upload mo picker file/folder nhu hien tai.
- Neu keo tha vao `dvgUploads`:
  - File/folder duoc add vao danh sach Waiting.
  - Bam Upload moi bat dau upload.
- `dvgUploads` nam cung vi tri `dgvDownloads`; radio:
  - `radlistdown`: show `dgvDownloads`, hide `dvgUploads`.
  - `radlistup`: show `dvgUploads`, hide `dgvDownloads`.
- Khi dang download/upload:
  - Khoa chuyen danh sach neu can.
  - Dang download thi phai hien download grid.
  - Dang upload thi phai hien upload grid.
  - Hoan tat moi cho doi lai.
- Upload hien progressbar/toc do/trang thai tuong tu download.
- Upload chua bat buoc resume, user dang chap nhan upload khong resume.

### 4.6 Delete / clear

- `btncleardrv`:
  - Neu khong chon dong nao trong danh sach downloaded: clear toan bo danh sach.
  - Neu chon 1/nhieu dong: chi xoa cac dong do.
- Delete remote file/folder da co tu ban dau, khong duoc khoa lai.
- Delete remote hien dang xoa vinh vien khi AgentServices thuc thi do service session/quyen.
- Da thao luan ve Recycle Bin:
  - Service chay session 0 nen dua vao Recycle Bin cua user dang login khong don gian.
  - Tam thoi chua lam AgentOsin/User-session helper.
- Nut xoa remote co password theo HHMM hien tai da co trong history.

### 4.7 Auto update AgentServices

- Da lam tinh nang update AgentServices.
- Control hien version Agent tren card.
- `lblver` hien version Control.
- Version duoc khai bao trong `AgentShared/AppVersion.cs`:
  - `CurrentVersionControl`
  - `CurrentVersionAgent`
- Update files nam trong:
  - `AgentControl/Updates/AgentServices/AgentServices.exe`
  - `AgentControl/Updates/AgentServices/AgentUpdater.exe`
  - `AgentControl/Updates/AgentServices/README.txt`
- Agent update root tren may Agent:
  - `C:\ProgramData\CaoData\AgentServices\Updates`
  - Marker/log cu trong `C:\ProgramData\Intel\Driver\Updates` duoc migrate mot lan de khong mat trang thai update dang do.
- AgentServices gui update status ve Control, dong thoi ghi log.
- Control co form/status rieng de xem tien trinh update tung Agent.
- Nhieu Agent update thi moi Agent co form/status rieng.
- AgentServices phan viec:
  - Nhan lenh update.
  - Kiem tra/tai file update.
  - Mo AgentUpdater.
  - Neu mo thanh cong thi het nhiem vu cua AgentServices.
- AgentUpdater phan viec:
  - Khoi dong.
  - Stop AgentServices.
  - Copy file moi.
  - Start AgentServices.
  - Cho AgentServices ket noi lai Control.
  - Thong bao status ve Control/log.

## 5. Cac toi uu UI/performance da lam gan day

### 5.1 `dgvDownloads`

Da fix hien tuong UI giat/nhay khi download:

- Timer update UI khong con ep `SuspendLayout/ResumeLayout` lien tuc cho grid.
- Chi set cell value/font/color neu gia tri that su thay doi.
- Bo `Refresh()`/`Update()` khong can thiet sau khi add queue.
- Khi add queue:
  - Chi auto-scroll neu nguoi dung dang o cuoi danh sach.
  - Neu nguoi dung dang xem vi tri khac thi khong ep scroll.
- Khi keo/resize form:
  - `Form1.WndProc` set `_isFormMovingOrSizing`.
  - `tmrUpdateUI_Tick` return som neu dang move/resize.

### 5.2 Card Agent / `ListBoxNHF`

Da fix card nhay khi click A/B, scroll, resize:

- File: `NHFUiControls/NHFUiControls/Class1.cs`
- Bo redraw thua khi doi selected card.
- Chan `WM_ERASEBKGND` de giam flicker nen.
- Gom invalidate trung; moi item chi invalidate 1 lan moi event.
- Khi scroll doc bang wheel/scrollbar:
  - Tam tat hover repaint.
  - Cuon xong moi tinh lai hover.
- Khi drag/resize form:
  - `Form1.WndProc` goi `ListboxAgents.SetVisualUpdatesSuspended(true/false)`.
  - Dung `WM_SETREDRAW` de tam khoa redraw card list.
- Khi add Agent moi:
  - Chi invalidate item moi, khong invalidate toan list.
- Muc tieu: neu sau nay co khoang 150 card Agent thi cuon/resize van muot hon.

## 6. Nhung loi da tung gap va can tranh lap lai

- SQLite `database is locked` khi thao tac download/update DB qua nhieu task.
- Download nhieu file bi treo file dau tien, file sau khong chay.
- Moi file download xong hien thong bao rieng; da toi uu thanh het batch moi thong bao.
- Folder download tung bi mat chuc nang do sua code nham; can tranh.
- Nhiu Agent:
  - Agent B tung load o dia dung nhung click vao lai hien folder cua Agent A.
  - Phai luon gan AgentID vao node/item/tag.
- Checksum:
  - Download local OK, remote Agent khac tung FAIL.
  - Khi sua stream/chunk/hash phai rat can than.
- Upload:
  - Co lan upload dung o Verifying 100%, khong copy file sang Agent.
  - Da fix, can tranh pha vo.
- Services:
  - `System.Management` can package/reference.
  - Service tung loi 1053 neu start cham/sai Windows service pattern.
  - Khi chay service, username co the thanh machine account `MACHINE$`; da can xu ly lay user dang login rieng.
- UI:
  - `dgvDownloads` va card Agent da tung nhay/giat do repaint thua.
  - Khong dung `Refresh()`/`Update()` lung tung trong timer/event loop neu khong can.
- Git:
  - Da don branch, xoa branch code moi nhat va `Complete-v1`.
  - Tu nay khong tao branch lung tung neu user khong yeu cau.

## 7. Build / publish / release

### Build debug

Thuong dung:

```powershell
dotnet build AgentControl\AgentControl.csproj
```

Neu bi lock file:

- Stop `AgentControl.exe`.
- Neu VS van giu `.pdb`/`.dll`, duoc phep stop `devenv`.
- Sau do build lai.

### Publish AgentServices self-contained single-file co nen

Release config trong `AgentServices/AgentServices.csproj`:

- `RuntimeIdentifier=win-x64`
- `SelfContained=true`
- `PublishSingleFile=true`
- `EnableCompressionInSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- `PublishTrimmed=false`
- `DebugType=embedded`
- `DebugSymbols=false`

### Publish AgentUpdater self-contained single-file co nen

Release config trong `AgentUpdater/AgentUpdater.csproj` tuong tu AgentServices.

### File can dem sang may Agent moi

Voi ban self-contained single-file:

- `AgentServices.exe`
- `appsettings.json` neu can cau hinh server/port/ket noi.

`AgentShared.pdb`, `*.Development.json` khong can cho may test/release binh thuong.

## 8. Huong dan khi chat moi tiep tuc

Khi mo chat moi:

1. Yeu cau Codex doc `PROJECT_CONTEXT.md`.
2. Yeu cau Codex chay:

```powershell
git status --short --branch
```

3. Neu can sua code:
   - Doc dung file lien quan.
   - Kiem tra cac chuc nang lien quan trong context nay truoc khi sua.
4. Neu can build:
   - Neu VS/app dang chay debug, stop VS/app truoc.
   - Build chuan, khong build output tam neu user muon test bang VS.

## 9. Cac file thuong hay dung

- Main form:
  - `AgentControl/Form1.cs`
  - `AgentControl/Form1.Designer.cs`
- SQLite helper:
  - `AgentControl/SQLiteHelper.cs`
- Progress bar cell:
  - `AgentControl/DataGridViewProgressBarCell.cs`
- Agent update UI/server:
  - `AgentControl/AgentUpdateServer.cs`
  - `AgentControl/AgentUpdateStatusForm.cs`
- Agent service:
  - `AgentServices/Worker.cs`
  - `AgentServices/AgentUpdateClient.cs`
  - `AgentServices/appsettings.json`
- Shared:
  - `AgentShared/AppVersion.cs`
  - `AgentShared/FileTransfer.cs`
  - `AgentShared/TransferFrameProtocol.cs`
  - `AgentShared/AgentUpdateModels.cs`
- Updater:
  - `AgentUpdater/Program.cs`
- Custom Agent card/listbox:
  - `NHFUiControls/NHFUiControls/Class1.cs`
- Update package folder:
  - `AgentControl/Updates/AgentServices/`

## 10. Trang thai mong muon sau moi lan sua

- Build khong error.
- Build Debug/Release hien phai dat 0 error, 0 warning; warning moi phai duoc phan loai va xu ly truoc khi ban giao.
- UI khong giat khi:
  - Keo/re-size window.
  - Download dang chay.
  - Scroll `dgvDownloads`.
  - Click/scroll card Agent.
- Download/upload/resume/checksum/update/delete van chay nhu truoc.

## 11. Trang thai don sach giao dien Backup - 2026-08-22

- Da don toan bo phan code tam lien quan den `frmSetBackup`, `btnSetBackup`, model/deploy backup va bang `BackupConfigs`; backup logic chua bat dau.
- Giu nguyen cac thay doi layout/resource/config hien tai cua user tren `frmToolBackup`; cac file `frmSetBackup.*` van o trang thai da xoa theo thay doi cua user.
- `tvRemoteFolders` da duoc bat checkbox de sau nay tan dung lam danh sach chon nguon backup, chua gan logic luu/deploy.
- GroupBox `Backup` hien co: duong dan + Browse, chu ky backup, chu ky full backup, gio chay, exclude folder, exclude extension/pattern, nut Them/Xoa va nut gui cau hinh.
- Da sua `NHFUiControls.ListBoxNHF` de forced redraw sau khi ket thuc `WM_SETREDRAW` va khong chan `WM_ERASEBKGND`, dong thoi bat double-buffer/ResizeRedraw cho form de xu ly ghost khi resize/restore cua `ListboxAgents`.
- Da build `AgentControl` thanh cong, 0 error; cac warning nullable/field cu cua project van con nhu truoc. Chua code nghiep vu backup.
- Da loai bo cac khai bao Designer khong duoc su dung (`lvAgents` va cot du lieu cu `A/B/C/D/E`); khong thay doi chuc nang dang chay.
- Git tren `main`, status sach neu user yeu cau commit/push.

## 12. Module Backup FIRST/INC - bat dau 2026-08-22

### 12.1 Checkpoint truoc khi lam

- Commit checkpoint: `d012772` - `TRUOC KHI LAM BACKUP`.
- Neu can doi chieu trang thai truoc module Backup thi dung commit nay; khong reset hard khi user chua yeu cau.

### 12.2 Kien truc da them

- Shared model/protocol rieng:
  - `AgentShared/BackupModels.cs`
  - Marker binary backup `0x04` trong `AgentShared/TransferFrameProtocol.cs`.
  - Cac khoi chen vao file dung chung deu co comment `BO SUNG MODULE BACKUP`.
- AgentControl:
  - `AgentControl/Form1.Backup.cs`: UI, lay checkbox `tvRemoteFolders`, exclude, Browse, Deploy va xu ly packet backup.
  - `AgentControl/BackupRepository.cs`: DB rieng `BackupManagement.db`, khong dung lock cua `AgentManagement.db`.
  - `AgentControl/BackupReceiver.cs`: nhan binary chunk va ghi truc tiep tung file xuong dia.
- AgentServices:
  - `AgentServices/BackupFileScanner.cs`: scan metadata va exclude folder/pattern.
  - `AgentServices/AgentBackupManager.cs`: luu config, scheduler, FIRST/INC, manifest va state.
  - `Worker.cs` chi noi packet/socket vao manager; cac khoi chen co comment `BO SUNG MODULE BACKUP`.

### 12.3 Hanh vi hien tai

- Control chon Agent, tick o dia/thu muc tren `tvRemoteFolders`, khai bao noi luu tren Control, chu ky, gio, exclude folder va exclude extension/pattern, sau do bam `Send Config Backup`.
- Khong cho backup toan bo root `C:\`; van cho chon Desktop hoac thu muc con cu the tren C.
- Pattern mac dinh khi chua co config: `.tmp`, `.temp`, `~*`, `~$*`; ap dung toan cuc.
- Config duoc luu trong bang `BackupConfigs` cua `BackupManagement.db` va gui xuong Agent.
- Agent ghi config vao node `BackupConfig` trong file `appsettings.json` tai `AppContext.BaseDirectory`; khong tao file config moi.
- Scheduler Agent kiem tra moi 30 giay, chi chay khi den gio va da du chu ky 1/2/... ngay.
- Lan backup dau tien: Agent upload vao `FIRST-{AgentID}.inprogress`; chi khi nhan du moi doi thanh `FIRST-{AgentID}-yyyy-MM-dd` theo ngay hoan tat.
- Cac lan tiep theo, ke ca ngay den chu ky full: Agent chi upload thay doi vao `INC-{AgentID}-yyyy-MM-dd`.
- Khi den chu ky full (mac dinh 60 ngay), manifest INC dat `CreateSyntheticFull=true`; Control tu dung mot `FIRST-{AgentID}-yyyy-MM-dd` moi tu inventory sau khi da nhan xong INC, Agent khong upload lai toan bo.
- Khong co session ID theo quyet dinh cua user; moi ngay toi da mot phien thanh cong.
- File duoc stream binary 256 KB/chunk, khong base64, khong nen thanh mot cuc.
- Noi luu tren Control:
  - `{ControlStoragePath}/{SessionName}/Files/D/...`
  - `{ControlStoragePath}/{SessionName}/manifest.json`
- Manifest co danh sach `Created`, `Modified`, `Deleted`, `Errors`.
- Control cap nhat `BackupFileInventory` gom ten, source path, relative path, size, last modified, deleted va session cap nhat.
- State incremental cua Agent nam tai `%ProgramData%/CaoData/AgentServices/BackupState/{AgentID}.json`; file legacy trong `%ProgramData%/Intel/Driver/BackupState` duoc copy mot lan. Day la runtime state, khong phai file config.
- Neu scan gap loi quyen truy cap thi van ghi vao manifest nhung khong ket luan file bi xoa, tranh note xoa nham.

### 12.4 Synthetic Full sau chu ky (bo sung 2026-08-24)

- Module rieng: `AgentControl/SyntheticFullBuilder.cs`; khong chen logic dung Full vao UI hay luong download/upload cu.
- Control doc `BackupFileInventory` theo tung lo 2.000 dong, khong nap toan bo inventory vao RAM.
- Voi moi file dang con hieu luc, Control lay ban moi nhat tu `UpdatedSession`:
  - uu tien tao hard link trong cung volume;
  - neu he thong file/volume khong cho hard link thi copy file noi bo tren Control.
- Full duoc dung trong `{SessionName}.building`; `manifest.json.tmp` duoc ghi streaming theo lo. Chi khi ghi xong moi doi thanh `manifest.json`, sau do doi thu muc `.building` thanh ten `FIRST-...` chinh thuc.
- Neu mat dien sau khi thu muc chinh thuc da duoc doi ten nhung truoc khi DB commit, lan sau Control thay `manifest.json` hoan chinh se khoi phuc moc session vao DB.
- Khi Synthetic Full hoan tat, tat ca inventory dang song duoc rebase `UpdatedSession` sang FIRST moi trong cung transaction DB. Vi vay chu ky INC moi khong con phu thuoc file vat ly cua chu ky cu.
- Control khong tu xoa FIRST/INC cu trong buoc nay; retention/don du lieu cu se thiet ke rieng de tranh xoa nham.
- Agent cho phep toi da 12 gio cho Control dung Synthetic Full; phien thuong van timeout 30 giay.
- Da chay integration test nho: FIRST cu + INC co sua/them/xoa -> FIRST moi co dung file, file da xoa khong con, manifest dung loai FIRST va inventory tro sang FIRST moi.

### 12.5 Chua lam / can test thuc te

- Da co restore file/folder ve mot thu muc tren may AgentControl; chua co luong day nguoc restore xuong AgentServices.
- Scanner hien quet metadata full va so sanh state. Chua doc USN Journal; code scanner da tach rieng de toi uu USN sau khi luong FIRST/INC duoc test on dinh.
- FIRST dau tien khong con dua toan bo danh sach `Created` vao goi SessionComplete. Control tao manifest streaming tu DB theo lo; Agent van scan metadata vao dictionary/state JSON de giu co dinh ke hoach FIRST qua restart.
- Da co bo integration test tu dong cho FIRST/INC/Synthetic Full/recovery/resume; xem muc 17. Van can test nghiem thu tren hai may that voi du lieu dai ngay truoc khi dua vao production:
  1. FIRST nhieu GB qua LAN/WAN, ngat mang/restart giua chung va resume.
  2. Sua/them/xoa file sau FIRST, kiem tra INC va Synthetic Full dung moc ngay.
  3. Kiem tra config thuc te duoc ghi vao appsettings cua Windows Service Agent dang chay.
- Khi debug cung may, phai stop Windows Service AgentServices de tranh hai instance cung AgentID da nhau/reconnect lien tuc.

### 12.6 FIRST resume lau ngay (bo sung 2026-08-24)

- FIRST ban dau dung ten lam viec on dinh `FIRST-{AgentID}` trong protocol va thu muc Control `FIRST-{AgentID}.inprogress`; khong dung session ID/ngay bat dau.
- Agent luu `FirstStartedAtUtc` va `PendingFirstInventory` vao state runtime hien co truoc khi upload. Sau restart/mat ket noi, Agent tiep tuc dung dung danh sach da scan lan dau; file moi phat sinh khong chen vao FIRST va se duoc INC sau phat hien.
- Truoc moi file, Agent gui `BACKUP_FIRST_FILE_RESUME_QUERY` kem source path, relative path, size va last modified hien tai. Control tra `BACKUP_FIRST_FILE_RESUME_INFO` gom `Offset`/`Completed`.
- File dang nhan tren Control co duoi `.partial`. Offset vat ly cua `.partial` la moc resume chinh; DB checkpoint moi 8 MB va khi query/final. Neu mat ket noi giua chunk, lan sau Agent seek dung byte Control dang co.
- DB `BackupManagement.db` co them bang rieng `FirstBackupRuns`, `FirstBackupFiles`, `FirstBackupSkipped` trong `AgentControl/FirstBackupStore.cs`; khong dung bang/lock cua DownloadQueue.
- Moi file nhan xong moi doi `.partial` thanh ten that, append mot dong vao `manifest.journal`, sau do danh dau `Completed` trong DB. File da Completed duoc bo qua khi resume.
- Neu mot file khong mo/doc/upload duoc, bi xoa, dang bi khoa, hoac doi size/last modified trong luc truyen: Agent gui `BACKUP_FIRST_FILE_SKIP`, Control xoa `.partial`/ban final cua rieng file do, ghi `Skipped` + reason vao DB/journal va tiep tuc file khac. Mat ket noi Control khong duoc tinh la Skipped; ca phien dung de resume.
- File Skipped khong duoc dua vao `Created`/`BackupFileInventory`; Agent luu `PendingFirstSkippedFiles` de khong thu lai sau restart. Khi FIRST chot, file nay bi loai khoi inventory Agent, nen neu no con ton tai thi INC ke tiep se xem la file moi va upload lai. Reason duoc ghi vao `Errors` cua manifest FIRST.
- Khi `Completed + Skipped == PlannedFileCount`, Control ghi `manifest.json.tmp` streaming theo lo 2.000 dong, doi thanh `manifest.json`, dat run `Finalizing`, roi doi thu muc theo ngay hoan tat thuc te:
  - vi du bat dau 2026-08-18, hoan tat 2026-08-22 -> `FIRST-{AgentID}-2026-08-22`.
- Sau khi doi ten, Control commit session/inventory. Neu mat dien giua doi thu muc va commit DB, trang thai `Finalizing` + manifest hoan chinh duoc dung de khoi phuc khi Agent ket noi lai.
- Neu Control da chot FIRST nhung Agent chua kip luu state/nhan ACK, lan ket noi lai Control xac nhan cac file da Completed va tra ket qua thanh cong de Agent chot `LastSuccessfulBackupUtc`/`LastFullBackupUtc`.
- Da integration test: gui 400.000 byte cua file 1, tao lai `BackupReceiver` nhu Control restart, resume dung offset 400.000, gui tiep file 1 + file 2, chot folder ngay hoan tat, kiem tra noi dung, journal, manifest va inventory deu dung.
- Da integration test Skipped: ke hoach 2 file, nhan thanh cong 1 file va bo qua 1 file dang khoa; FIRST van chot, manifest `Created` chi co 1, `Errors` co reason cua file bi bo qua, inventory chi co file da backup.

## 13. AgentControl chay nen/System Tray - 2026-08-24

- `AgentControl.csproj` da doi `OutputType` tu `Exe` sang `WinExe`, vi vay AgentControl khong con mo kem cua so console mau den. Thay doi nay chi anh huong cach hien thi khi khoi dong, khong thay doi socket, backup hay cac chuc nang hien co.
- Module tray duoc tach rieng tai `AgentControl/Form1.Tray.cs`; `Form1.cs` chi goi `InitializeTrayModule()` sau khi khoi tao cac module hien co.
- Bam nut X tren cua so AgentControl se an form va bo khoi taskbar, nhung chuong trinh/server van tiep tuc chay trong system tray.
- Nhan dup icon tray hoac chuot phai chon `Mo AgentControl` de hien lai cua so.
- Chuong trinh chi thoat that khi chuot phai icon tray chon `Exit`; khi Windows shutdown van cho phep form dong binh thuong.

## 14. Ngan system sleep khi truyen du lieu - 2026-08-24

- Module dung chung `AgentShared/SystemSleepBlocker.cs` dung Windows Power Request (`SystemRequired`) theo co che dem tham chieu, ho tro nhieu tac vu truyen file chay dong thoi.
- Cac ham native `PowerCreateRequest`, `PowerSetRequest`, `PowerClearRequest` phai import tu `Kernel32.dll` (khong phai `PowrProf.dll`). Da test runtime tao/giai phong power request thanh cong ngay 2026-08-25.
- Chi yeu cau Windows khong dua may vao sleep; khong dung `DisplayRequired`, vi vay man hinh van duoc phep tat theo thoi gian da cai trong Windows.
- Power Request khong ngan shutdown/restart. Khi chuong trinh/tac vu ket thuc, request duoc clear va dispose.
- AgentControl giu mot sleep block trong suot vong doi `Application.Run`; thu xuong tray van tiep tuc chan system sleep cho den khi chon `Exit`.
- AgentServices chi chan sleep trong cac khoang sau:
  - Control upload file xuong Agent: giu tu chunk dau den chunk cuoi; neu loi/mat ket noi/service stop thi tu giai phong.
  - Control download file tu Agent: giu trong suot qua trinh checksum/doc/gui file.
  - FIRST/INC backup: giu trong toan bo phien quet, upload va cho Control chot ket qua.

## 15. Khoi phuc file backup ve may AgentControl - 2026-08-25

### 15.1 Checkpoint va pham vi

- Checkpoint truoc khi lam restore: commit `757753a` - `TRƯỚC KHI LÀM KHÔI PHỤC`.
- Pham vi hien tai: chon file/folder trong ban backup va trich xuat ve mot thu muc tren may AgentControl; khong day file nguoc xuong AgentServices va khong mirror/xoa file dich.
- Nut `btnrecovery` tren form chinh yeu cau chon Agent, sau do mo modal `frmRecovery`. Phan noi nut duoc tach tai `AgentControl/Form1.Recovery.cs`.

### 15.2 Chon ngay va dung snapshot

- Khi `frmRecovery` load, doc `ControlStoragePath` cua Agent va chi quet folder hoan chinh co dang:
  - `FIRST-{AgentID}-yyyy-MM-dd`
  - `INC-{AgentID}-yyyy-MM-dd`
- Chi folder co `manifest.json` va 10 ky tu cuoi parse dung `yyyy-MM-dd` moi duoc dua vao `cbxlistday`; `.inprogress`, `.building` va folder loi bi bo qua.
- Moi ngay chi hien mot dong trong ComboBox, ke ca ngay do co ca INC va Synthetic FIRST.
- `RecoverySnapshotBuilder.cs` chon FIRST moi nhat tai/truc ngay user chon, sau do replay cac INC phat sinh sau FIRST den ngay do. Synthetic FIRST cung ngay duoc xem la moc da gom INC truoc no.
- `BackupManifestStreamReader` doc JSON theo buffer va tung entry; khong deserialize toan bo manifest lon vao RAM.
- Snapshot duoc index vao DB rieng `RecoverySnapshot.db` qua `RecoverySnapshotRepository.cs`. Build dung transaction; neu loi/mat dien thi khong de lai snapshot nua voi.
- Signature dua tren ten/size/last-write cua cac manifest giup mo lai cung ngay nhanh, chi rebuild khi chain manifest thay doi.

### 15.3 Giao dien va lua chon

- `TvBackupFile` la cay ao/lazy-load tu SQLite, co checkbox folder. Root hien theo o dia (`D:\`, ...).
- Click folder se nap file truc tiep trong folder do vao `lvBackupFiles`, gom ten, size, extension va last modified; ListView co checkbox chon file rieng.
- Tick folder dai dien cho toan bo file con, ke ca cac node chua expand. Lua chon folder/file duoc dua vao bang staging theo RunID de query theo batch, khong tao danh sach khong lo trong RAM.
- `btnbrowsepathbk` chon thu muc dich tren AgentControl va ghi vao `txtpathsavebk`.
- Chan chon thu muc dich nam ben trong `ControlStoragePath` de khong ghi de/lam ban cac ban backup goc.

### 15.4 Trich xuat va an toan

- `RecoveryFileExtractor.cs` doc danh sach da chon theo batch 500 file, copy buffer 1 MB va cap nhat `pcbbackup` theo tong byte.
- Giu cau truc o dia trong thu muc dich, vi du `D:\Data\a.txt` thanh `{ThuMucDich}\D\Data\a.txt`.
- Moi file ghi vao `{ten}.restoring`; sidecar `.restoring.meta` luu source session, relative path, size va last modified. Chi resume neu metadata trung khop, tranh noi nham partial cua ngay/session khac.
- Nhan du moi set last-write va atomically doi `.restoring` thanh ten that. Neu file dich ton tai, user duoc hoi xac nhan mot lan va file se bi ghi de sau khi ban tam hoan chinh.
- Neu dong form khi dang copy, form hoi xac nhan, cancel an toan va giu `.restoring` de tiep tuc lan sau.
- Khi dang chay ProgressBar dung trang thai mac dinh; hoan tat dat 100%, doi sang trang thai paused mau vang/cam cua Windows va hien tong file thanh cong/loi.

### 15.5 Test da chay

- Integration FIRST + INC: file tao moi, sua, xoa duoc replay dung; file sua tro dung ve folder INC, file xoa khong con trong snapshot.
- Chon folder bang SQL staging, thong ke file/byte va trich xuat noi dung/cau truc dung.
- Cache cung ngay va resume file partial da test thanh cong.
- Manifest FIRST 100.000 entry duoc ghi streaming, parser/index doc du 100.000 file ma khong deserialize ca manifest.
- Manifest co `..\evil.txt` bi tu choi va transaction build rollback.
- Resume metadata dung tiep tuc dung offset; metadata khac session bi reset thay vi ghep nham du lieu.

## 16. Nap cau hinh backup rieng khi chon Agent - 2026-08-25

- Moi lan `ListboxAgents` doi lua chon, module Backup lay `AgentID` truc tiep tu item dang chon va doc cau hinh tu bang `BackupConfigs` trong `BackupManagement.db`.
- UI xoa ngay cau hinh cua Agent truoc trong luc doc DB, tranh hien hoac vo tinh deploy nham cau hinh cua may khac.
- Moi luot nap co version rieng; neu user doi Agent nhanh, ket qua DB cua Agent cu ve tre se bi bo qua.
- Cau hinh duoc nap lai gom: duong dan luu tren Control, chu ky INC, chu ky Full, gio backup, thu muc loai tru, extension/pattern loai tru va cac checkbox nguon backup tren `tvRemoteFolders`.
- Agent chua co cau hinh se ve mac dinh: duong dan rong, Full 60 ngay, backup moi 1 ngay, gio `00:00`, khong tick nguon, exclude folder rong va cac pattern mac dinh `.tmp`, `.temp`, `~*`, `~$*`.
- Da test runtime voi hai Agent co cau hinh khac nhau: nap dung tung Agent, cap nhat Agent A khong anh huong Agent B, Agent chua cau hinh tra ve `null` de UI dung mac dinh.

## 17. Dot toi uu, hardening va test toan he thong - 2026-08-25

### 17.1 Git checkpoint va nguyen tac pham vi

- Checkpoint truoc khi sua: commit `e0527b4` - `TRUOC KHI TOI UU VA FIX TOAN BO`.
- Tag checkpoint: `checkpoint-truoc-toi-uu-fix-20260825`.
- Dot nay giu nguyen UI va hanh vi cu; module moi duoc tach thanh file/class rieng khi co the. Cac khoi bat buoc chen vao code dung chung co comment phan dinh.
- Project test rieng `AgentIntegrationTests` da duoc them vao solution; khong chen test/debug hook vao executable production.

### 17.2 Bao mat va do ben giao thuc

- Toan bo ket noi Control/Agent/Updater dung TLS 1.2/1.3 va challenge hai chieu bang PSK/HMAC-SHA256 gan voi certificate cua phien TLS.
- AgentID dung cho moi binary/JSON packet phai trung AgentID da xac thuc; peer khong the tu doi AgentID sau handshake.
- Shared key doc tu `CAODATA_SHARED_KEY` truoc, sau do moi toi:
  - Control: `ConnectionSecurity:SharedKey` trong `AgentControl/appsettings.json`.
  - Agent: `ConnectionConfig:SharedKey` trong `AgentServices/appsettings.json`.
- Key phai it nhat 32 ky tu va phai giong nhau tren Control/Agent. Key dang commit chi la key deploy mac dinh; khi dua production phai thay key rieng tren tat ca may, uu tien environment variable de khong commit secret.
- Control tu tao certificate server tai `%LocalAppData%/CaoData/AgentControl/AgentControl.transport.pfx`; file cat ngan/hong duoc doi ten `.corrupt-*` va tao lai an toan.
- `TransferFrameProtocol` gioi han JSON frame 16 MB, binary header 1 MB va binary body 8 MB; JSON receive dung pool buffer de tranh cap phat RAM lon lien tuc.
- `AgentShared/PathSafety.cs` chan rooted path, `..`, ADS (`:`), ten thiet bi DOS, separator lap, ky tu filename khong hop le va segment co dau cham/khoang trang cuoi; van giu dung ten hop le co khoang trang dau.
- Cac luong ghi file dung chung `AgentShared/ResumableTransferFile.cs`: kiem tra offset/total/chunk, truncate khi offset 0, flush den dia va ho tro file 0 byte.

### 17.3 Duong dan du lieu va SQLite

- Du lieu Control khong con phu thuoc working directory:
  - `%LocalAppData%/CaoData/AgentControl/AgentManagement.db`
  - `%LocalAppData%/CaoData/AgentControl/BackupManagement.db`
  - `%LocalAppData%/CaoData/AgentControl/RecoverySnapshot.db`
  - co the override bang `CAODATA_CONTROL_DATA_ROOT` khi test/deploy.
- Du lieu runtime Agent nam tai `%ProgramData%/CaoData/AgentServices`; co the override bang `CAODATA_AGENT_DATA_ROOT`.
- DB cu o working directory/AppContext duoc migrate bang SQLite Backup API, vi vay ca transaction da commit nhung con trong WAL cung duoc mang theo; khong chi copy rieng file `.db`.
- SQLite dung WAL, busy timeout va synchronous FULL cho du lieu queue/backup can do ben.
- Backup config tren Control chi commit DB sau khi Agent tra ACK thanh cong; tranh UI/DB bao da deploy trong khi Agent ghi appsettings that bai.
- Ghi appsettings, Agent backup state, manifest metadata va certificate deu dung file tam + flush + move de giam nguy co file JSON/PFX bi cat ngan khi mat dien.

### 17.4 FIRST, INC va Synthetic Full

- FIRST co ke hoach co dinh qua restart (`PendingFirstInventory`) va resume tung file tu offset that tren Control. File moi sinh trong luc FIRST duoc de cho INC ke tiep.
- FIRST rong (0 file) gio van chot thanh snapshot hop le; state co co rieng `InitialBackupCompleted` va `PendingFirstPlanInitialized` de phan biet chua scan voi scan xong nhung rong.
- File bi khoa, bi xoa, khong doc duoc hoac thay doi trong luc FIRST duoc skip rieng; phien tiep tuc. File do khong vao inventory va se duoc nhin nhu file moi/thay doi o INC sau.
- FIRST chi dat ngay sau khi tat ca file Planned da Completed/Skipped. Neu bat dau 2026-08-18 va xong 2026-08-22 thi folder la `FIRST-{AgentID}-2026-08-22`.
- DB/journal/manifest sidecar cho phep khoi phuc ca hai diem crash: truoc khi doi folder final va sau khi doi folder nhung truoc DB commit.
- Manifest FIRST duoc ghi streaming theo lo, da test 100.000 entry; khong gom ca manifest vao RAM. `session.json` luu danh tinh va SHA-256 cua manifest de phat hien manifest bi thay the ke ca khi size/timestamp bi gia lap giong cu.
- Moi file backup co SHA-256 trong manifest/inventory. Control chi chap nhan Created/Modified neu file vat ly da nhan du, dung size va dung hash.
- INC khong duoc downgrade mot session da Success thanh Failed khi packet retry/ACK bi mat den sau.
- Scanner bo qua reparse point/junction de tranh vong lap va vuot khoi nguon da chon.
- Neu scanner gap access/I/O error, inventory cu bi thieu trong vung scan duoc giu lai thay vi bi xoa ngam; nhu vay INC sau van co the phat hien delete khi quet sach tro lai. Danh sach loi chi giu toi da 1.000 dong chi tiet + mot dong tong hop de khong lam phinh frame/manifest.
- Synthetic Full dung folder `.building`, hard link neu cung volume va copy neu can. File INC moi duoc nhan vao `.incoming` roi moi replace, nen retry INC khong sua noi dung inode da hard-link vao FIRST cu.
- Synthetic Full chi rebase inventory sau khi folder va manifest hoan chinh; loi Synthetic khong rollback INC da commit. Khoi dong lai co the chot tiep folder final hop le dua tren sidecar/hash.
- Retention 60 ngay hien la chu ky tao Synthetic Full, chua tu xoa cac FIRST/INC cu.

### 17.5 Recovery, upload/download va lifecycle

- Recovery replay FIRST + INC streaming, index SQLite, staging theo batch va extract qua `.restoring`; kiem tra SHA-256 truoc khi replace file dich. Loi/traversal rollback snapshot/index thay vi de du lieu nua voi.
- Download resume doi chieu DB offset voi kich thuoc file that; file thieu/dai/ngan bat thuong duoc reset dung cach. Neu job resume cu de checksum None thi tu nang len SHA-256 de xac minh noi dung.
- Agent giu source file bang `FileShare.Read` trong luc checksum/gui de noi dung khong bi thay doi giua hash va transfer.
- Download folder gui danh sach theo page va co backpressure; Control khong fallback sang duong dan local neu Agent khong tra du lieu.
- Pending upload/download bi fail/wait dung trang thai khi socket mat; UI handler bat exception de khong lam crash WinForms.
- Listener, heartbeat va reconnect co cancellation/cleanup ro rang. Heartbeat cua socket cu khong con ghi de trang thai socket moi cung AgentID.
- Control dispose listener/socket/task/tray khi Exit; dong nut X chi an xuong tray. Control la `WinExe`, khong mo console den.
- `SystemSleepBlocker` van cho shutdown va tat man hinh, chi chan system sleep theo dung pham vi da ghi o muc 14.

### 17.6 Auto update AgentServices

- `AgentUpdater/AgentUpdateWorkflow.cs` tach workflow copy/rollback khoi CLI de test doc lap.
- Updater xac minh SHA-256, stop service, tao backup, replace, start lai; neu copy/start that bai thi rollback executable cu va thu start lai.
- Marker/log update chuyen sang `%ProgramData%/CaoData/AgentServices/Updates`; artifact legacy `Intel/Driver/Updates` duoc migrate mot lan.
- Control/Agent/Updater deu dung cung TLS + PSK. Port, timeout, path, hash va tham so CLI duoc validate.
- `AgentControl.csproj` tu copy ba file trong `AgentControl/Updates/AgentServices` vao output Debug/Release/publish. Sau khi sua Agent/Updater phai publish lai va thay hai EXE source nay.
- Goi da publish ngay 2026-08-25:
  - `AgentServices.exe`: SHA-256 `C6C33096F7986809F89F2A18069357056F6009D53B97B8730FFABE8FB1A00536`.
  - `AgentUpdater.exe`: SHA-256 `C453EBE43227EB0AA397E0C3D7A48466B4420C07BC6445C8E460727C07412C8A`.

### 17.7 Ket qua test ngay 2026-08-25

- `dotnet build AgentControl\AgentControl.sln -c Debug --no-restore`: 0 error, 0 warning.
- `dotnet build AgentControl\AgentControl.sln -c Release`: 0 error, 0 warning.
- `dotnet test AgentIntegrationTests\AgentIntegrationTests.csproj -c Debug`: 57/57 pass.
- `dotnet test AgentIntegrationTests\AgentIntegrationTests.csproj -c Release --no-build`: 57/57 pass.
- Chay lap toan bo suite Debug 3 lan lien tiep (171 test case executions): 171/171 pass, khong thay race/flaky failure.
- `dotnet list ... package --vulnerable --include-transitive`: khong co package co lo hong theo NuGet source hien tai.
- `dotnet list ... package --deprecated`: khong co package deprecated.
- Test bao phu: TLS dung/sai PSK; frame/path validation; upload/download resume va file 0 byte; scanner/exclude/C-root/reparse; FIRST rong/resume/skip/hash/ACK loss/power-loss; INC create/modify/delete; Synthetic Full/hard-link/crash; manifest 100.000 entry/tamper; recovery replay/extract/traversal/corruption; DB WAL migration; updater success va rollback; icon Windows.
- Smoke process Release:
  - AgentControl khoi tao UI, cac DB va certificate tren data root tam, song on dinh cho den khi test dung dung process.
  - AgentServices self-contained single-file khoi dong, tao backup state tren data root tam va tiep tuc reconnect binh thuong.
- Luu y: Control chi mo port 9000 sau khi bam nut `Ket noi Agent`; smoke startup khong tu dong bam nut UI.

### 17.8 Gioi han con lai / test nghiem thu can lam tren may that

- Scanner hien van full-scan metadata, chua dung USN Journal. Day la lua chon on dinh hien tai; USN chi nen lam sau khi co benchmark o dia NTFS that va fallback cho volume khong ho tro.
- Automated test khong thay the duoc test nhieu ngay tren hai may vat ly/Windows Service that, mat mang WAN, restart service/Control va mat dien cuong buc trong FIRST vai tram GB.
- Restore hien chi extract ve AgentControl; chua day nguoc xuong Agent.
- Upload Control -> Agent van khong resume theo quyet dinh hien tai; download va FIRST backup co resume.
- Chua co retention/xoa backup cu. Khong tu xoa FIRST/INC de tranh mat du lieu truoc khi user chot chinh sach.
- INC SessionComplete van la mot JSON frame; voi lich 1-2 ngay va quy mo thay doi thuong duoi 1.000 file thi du suc. FIRST lon da dung manifest streaming. Neu sau nay mot INC co the vuot 16 MB metadata thi can mo rong INC thanh protocol paged.
- Truoc production phai doi PSK deploy, mo firewall/NAT dung port 9000, bam `Ket noi Agent` tren Control, va test update Windows Service bang mot Agent staging truoc khi rollout tat ca Agent.

### 17.9 Fix trung WinForms resource cua frmToolBackup - 2026-08-25

- Da tai hien loi MSBuild `MSB3577`: `Form1.resx` va file rong `Form1.Tray.resx` cung bi SDK suy ra logical name `AgentControl.frmToolBackup.resources`, nen hai output resource trung nhau.
- Da xoa `Form1.Tray.resx` rong do Designer phat sinh; file nay khong chua icon, chuoi hay tai nguyen can giu.
- `AgentControl.csproj` nest cac module partial `Form1.Backup.cs`, `Form1.Lifetime.cs`, `Form1.Recovery.cs`, `Form1.Tray.cs` duoi `Form1.cs` va loai `Form1.*.resx` khoi `EmbeddedResource`. Quy tac nay ngan loi tai phat ngay ca khi Visual Studio lai sinh file `.resx` rong cho mot module partial.
- Da thu co y tao lai `Form1.Tray.resx`, clean/rebuild van thanh cong va danh sach resource sau MSBuild chi con `Form1.resx`, `frmRecovery.resx`, `Properties/Resources.resx`; sau test da xoa file thu.
- Da clean/rebuild toan solution Debug va chay lai 57 integration test de xac nhan ban sua metadata khong anh huong chuc nang.
