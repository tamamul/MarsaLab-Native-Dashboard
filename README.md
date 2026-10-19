# MARSA Lab — Dashboard Desktop Native (.NET 10)

Arsitektur tetap sederhana untuk jaringan lokal 21–40 komputer:

- **PC meja guru:** `MarsaLab.Admin` (server ASP.NET Core .NET 10) + `MarsaLab.Desktop` (dashboard Windows native).
- **PC lab:** `MarsaLab.Client`.
- Dashboard guru menggunakan kontrol Windows Forms asli. **Tidak menggunakan WebView2 dan tidak membuka browser** untuk menampilkan dashboard.

## Fitur dashboard desktop
- Daftar komputer, status Online/Offline, IP, waktu terakhir terlihat, pencarian dan filter.
- Pilih beberapa komputer atau semua komputer Online.
- Kirim pengumuman, kunci layar, buka website, buka halaman tugas, restart/shutdown terjadwal dan batalkan perintah daya.
- Riwayat perintah.
- Bagikan file ke siswa, hapus file bersama, lihat tugas terkumpul, unduh satu tugas atau seluruh tugas dalam ZIP.
- Pantau layar satu komputer dan kontrol mouse/keyboard secara opsional setelah kontrol diaktifkan.

## Persyaratan
- Windows 10/11 64-bit.
- .NET SDK 10 jika build dari source; installer rilis menggunakan self-contained publish.
- PC admin sebaiknya memakai IP tetap, misalnya `192.168.0.10`.
- Semua komputer berada di jaringan lokal yang saling terhubung.

## Token
Installer guru membuat token admin dan token client yang berbeda. Simpan keduanya dengan aman.

Untuk konfigurasi manual, atur `src/MarsaLab.Admin/appsettings.json`:
- `Lab:Token`: token dashboard admin minimal 24 karakter.
- `Lab:ClientToken`: token client minimal 24 karakter dan berbeda dari token admin.
- `Lab:Port`: default 5080.

Atur `src/MarsaLab.Client/appsettings.json` di setiap komputer lab:
- `ServerUrl`: `http://192.168.0.10:5080`
- `Token`: sama dengan `Lab:ClientToken` pada server.
- `ClientId`: kosong untuk memakai hostname atau isi ID unik.
- `HeartbeatSeconds`: 5.

## Build
Jalankan dari root repositori:

```powershell
dotnet publish .\src\MarsaLab.Admin\MarsaLab.Admin.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\guru
dotnet publish .\src\MarsaLab.Desktop\MarsaLab.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\guru
dotnet publish .\src\MarsaLab.Client\MarsaLab.Client.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\dist\client
```

Workflow GitHub Actions juga menghasilkan installer `MarsaLab-Guru-Setup.exe` dan `MarsaLab-Client-Setup.exe`.

## Keamanan dan batasan
- Uji perintah daya dan remote control pada satu PC percobaan terlebih dahulu.
- Restart/shutdown menggunakan jeda dan pemberitahuan dari client; pembatalan hanya berhasil jika perintah Windows masih terjadwal dan izin mencukupi.
- Client harus berjalan pada sesi pengguna Windows aktif untuk menampilkan pengumuman dan menjalankan kontrol interaktif.
- Server memakai HTTP pada LAN. Jangan port-forward port 5080 ke internet. Batasi Windows Firewall ke profil Private/Domain jaringan lab.
- `wwwroot` masih disimpan di server untuk kompatibilitas, tetapi dashboard native tidak memuat halaman tersebut melalui WebView.
