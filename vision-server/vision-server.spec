# -*- mode: python ; coding: utf-8 -*-
import os
import sys
import mediapipe

_MP_DIR = os.path.dirname(mediapipe.__file__)
# mediapipe.tasks.python.core.mediapipe_c_bindings가 importlib.resources.files(
# 'mediapipe.tasks.c')로 이 네이티브 라이브러리를 런타임에 동적으로 찾는다 - import문으로
# 안 잡히니 PyInstaller 정적 분석이 못 찾고, 그냥 두면 번들 실행 시
# "ModuleNotFoundError: No module named 'mediapipe.tasks.c'"로 죽는다(실측). resources.files가
# 찾는 것과 같은 상대 경로(mediapipe/tasks/c/)에 그대로 넣어줘야 한다.
# 파일 이름은 OS별로 다르다(mediapipe_c_bindings.py의 load_raw_library와 동일한 분기) -
# 이 스펙은 macOS에서 검증했고, Windows에서 PyInstaller를 돌리면 이 분기가 자동으로
# libmediapipe.dll을 골라 넣는다(별도 수정 불필요).
if sys.platform == "darwin":
    _MP_C_LIB_NAME = "libmediapipe.dylib"
elif sys.platform == "win32":
    _MP_C_LIB_NAME = "libmediapipe.dll"
else:
    _MP_C_LIB_NAME = "libmediapipe.so"
_MP_C_DYLIB = os.path.join(_MP_DIR, "tasks", "c", _MP_C_LIB_NAME)

a = Analysis(
    ['main.py'],
    pathex=[],
    binaries=[(_MP_C_DYLIB, 'mediapipe/tasks/c')],
    # models/*.task는 main.py가 __file__ 상대 경로가 아니라 sys._MEIPASS 기준으로
    # 런타임에 직접 읽어들이는 데이터 파일이라 PyInstaller의 정적 임포트 분석으로는
    # 자동으로 안 잡힌다 - 여기서 명시적으로 넣어줘야 한다.
    datas=[('models', 'models')],
    # mediapipe.tasks.c도 마찬가지로 importlib.resources를 통한 문자열 기반 동적
    # 임포트라 정적 분석이 놓친다 - 패키지 자체(__init__.py)도 명시해야 한다.
    hiddenimports=['mediapipe.tasks.c'],
    hookspath=[],
    hooksconfig={},
    runtime_hooks=[],
    # matplotlib은 main.py가 직접 쓰진 않지만, mediapipe.tasks.python.vision 패키지의
    # __init__이 drawing_styles -> drawing_utils를 무조건 임포트하면서 최상단에서
    # matplotlib을 하드 임포트한다(실측: excludes에 넣었더니 ModuleNotFoundError로 아예
    # 기동 실패) - 뺄 수 없는 진짜 의존성이라 excludes는 쓰지 않는다. 첫 실행 시
    # "building the font cache" 메시지와 함께 수 초~수십 초가 걸릴 수 있음(실측) - 게임
    # 실행 시마다인지 머신당 1회성인지는 추가 실측 필요.
    excludes=[],
    noarchive=False,
    optimize=0,
)
pyz = PYZ(a.pure)

exe = EXE(
    pyz,
    a.scripts,
    [],
    exclude_binaries=True,
    name='vision-server',
    debug=False,
    bootloader_ignore_signals=False,
    strip=False,
    upx=True,
    console=True,
    disable_windowed_traceback=False,
    argv_emulation=False,
    target_arch=None,
    codesign_identity=None,
    entitlements_file=None,
)
coll = COLLECT(
    exe,
    a.binaries,
    a.datas,
    strip=False,
    upx=True,
    upx_exclude=[],
    name='vision-server',
)
