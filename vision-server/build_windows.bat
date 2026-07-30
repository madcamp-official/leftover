@echo off
REM vision-server를 Windows용 실행파일로 패키징한다 - PyInstaller는 크로스 컴파일이
REM 안 돼서(맥에서 빌드한 게 윈도우에서 안 돌아감) 이 스크립트는 반드시 Windows 머신에서
REM 직접 실행해야 한다. 사전 준비: Python 3.9 이상 설치(https://www.python.org/downloads/,
REM 설치 화면에서 "Add python.exe to PATH" 체크 필수).
setlocal
cd /d "%~dp0"

if not exist .venv (
    echo [1/4] 가상환경 생성...
    python -m venv .venv
    if errorlevel 1 (
        echo Python이 설치 안 됐거나 PATH에 없습니다. python.org에서 설치 후 다시 실행하세요.
        pause
        exit /b 1
    )
)

echo [2/4] 가상환경 활성화 + 의존성 설치...
call .venv\Scripts\activate.bat
pip install -r requirements.txt
pip install pyinstaller

if not exist models\face_landmarker.task (
    echo [3/4] models\face_landmarker.task가 없어서 받는 중...
    curl -L -o models\face_landmarker.task "https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/latest/face_landmarker.task"
) else (
    echo [3/4] models\face_landmarker.task 이미 있음 - 건너뜀
)

echo [4/4] PyInstaller 빌드 (vision-server.spec)...
pyinstaller vision-server.spec --noconfirm --clean

echo.
if exist dist\vision-server\vision-server.exe (
    echo 성공: dist\vision-server\ 에 vision-server.exe가 생겼습니다.
    echo 확인: dist\vision-server\vision-server.exe --check-models
    echo 다음 단계: Unity Editor에서 Tools ^> UGAUGA ^> Build Dev Player (Windows)를
    echo 실행하면 이 폴더를 빌드 결과물 옆에 자동으로 복사합니다.
) else (
    echo 실패한 것 같습니다 - 위 로그에서 오류를 확인하세요.
)
pause
