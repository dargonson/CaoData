# PROJECT CONTEXT - Tool Backup / AgentControl / AgentServices

File nay dung de chuyen tiep sang cuoc tro chuyen Codex moi. Khi bat dau chat moi, hay bao Codex:

> Doc `PROJECT_CONTEXT.md`, kiem tra repo, roi tiep tuc lam viec theo context trong file nay.

## 1. Thong tin repo hien tai

- Workspace: `C:\Users\DoThai\Desktop\repoC#`
- Branch lam viec hien tai: `main`
- Remote branch hien tai: `origin/main`
- Trang thai Git sau lan don gan nhat:
  - Da hop nhat branch code moi nhat vao `main`.
  - Da xoa local branch code moi nhat.
  - Da xoa remote branch code moi nhat.
  - Da xoa branch `Complete-v1`.
  - Tu nay chi lam truc tiep tren `main`.
- Commit dau hien tai cua `main`: `2da3055` - `Toi uu, fix lag khi keo re giao dien`
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
    - `AgentUpdateRootDirectory = @"C:\ProgramData\Intel\Driver\Updates"`
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
  - `C:\ProgramData\Intel\Driver\Updates`
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
- Neu co warning cu thi co the de lai, nhung phai noi ro.
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
- State incremental cua Agent nam tai `%ProgramData%/Intel/Driver/BackupState/{AgentID}.json`; day la runtime state, khong phai file config.
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

- Chua lam restore theo dung pham vi da chot.
- Scanner hien quet metadata full va so sanh state. Chua doc USN Journal; code scanner da tach rieng de toi uu USN sau khi luong FIRST/INC duoc test on dinh.
- FIRST dau tien khong con dua toan bo danh sach `Created` vao goi SessionComplete. Control tao manifest streaming tu DB theo lo; Agent van scan metadata vao dictionary/state JSON de giu co dinh ke hoach FIRST qua restart.
- Da build full solution thanh cong, 0 error. Can test thuc te voi mot thu muc nho truoc:
  1. FIRST co file + manifest dung.
  2. Sua/them/xoa file, doi lich sang ngay hop le hoac state test, kiem tra INC.
  3. Kiem tra config thuc te duoc ghi vao appsettings cua ban Agent dang chay.
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
- Chi yeu cau Windows khong dua may vao sleep; khong dung `DisplayRequired`, vi vay man hinh van duoc phep tat theo thoi gian da cai trong Windows.
- Power Request khong ngan shutdown/restart. Khi chuong trinh/tac vu ket thuc, request duoc clear va dispose.
- AgentControl giu mot sleep block trong suot vong doi `Application.Run`; thu xuong tray van tiep tuc chan system sleep cho den khi chon `Exit`.
- AgentServices chi chan sleep trong cac khoang sau:
  - Control upload file xuong Agent: giu tu chunk dau den chunk cuoi; neu loi/mat ket noi/service stop thi tu giai phong.
  - Control download file tu Agent: giu trong suot qua trinh checksum/doc/gui file.
  - FIRST/INC backup: giu trong toan bo phien quet, upload va cho Control chot ket qua.
