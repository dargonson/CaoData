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
- Git tren `main`, status sach neu user yeu cau commit/push.
