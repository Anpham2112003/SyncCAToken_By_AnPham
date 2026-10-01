@echo off
chcp 65001 > nul
title Cai dat chung chi bao mat cho SyncCAToken

echo =====================================================================
echo    CAI DAT CHUNG CHI BAO MAT HTTPS CHO SYNCCATOKEN USB ADAPTER
echo =====================================================================
echo.
echo Dang kiem tra quyen Administrator...
net session >nul 2>&1
if %errorlevel% equ 0 goto :RUN_INSTALL

echo [LOI] Ban can chay file nay voi quyen Quan tri vien (Administrator).
echo.
echo HUONG DAN:
echo  1. Nhap chuot PHAI vao file "CaiDatChungChi.bat"
echo  2. Chon "Run as administrator" (Chay duoi quyen quan tri vien)
echo.
pause
exit /b 1

:RUN_INSTALL
echo Dang cai dat chung chi bao mat vao he thong Windows (Trusted Root)...
certutil -addstore -f "Root" "%~dp0localhost.cer"

if %errorlevel% neq 0 goto :INSTALL_FAILED

echo.
echo =====================================================================
echo [THANH CONG] Da cai dat chung chi bao mat HTTPS hoan tat!
echo Cac trinh duyet (Chrome, Edge) tren may tinh nay gio day se tu dong
echo tin cay ket noi ky so USB Token tai cong https://localhost:7054
echo =====================================================================
goto :END

:INSTALL_FAILED
echo.
echo [THAT BAI] Co loi xay ra trong qua trinh cai dat chung chi.

:END
echo.
pause