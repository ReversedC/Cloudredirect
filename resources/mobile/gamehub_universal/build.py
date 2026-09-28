import os
import subprocess
import zipfile
import shutil

ROOT = r"C:\Users\admin\Documents\CloudRedirect\resources\mobile\gamehub_universal"
SDK = r"C:\Users\admin\AppData\Local\Android\Sdk"
BUILD_TOOLS = os.path.join(SDK, "build-tools", "35.0.0")
PLATFORM_JAR = os.path.join(SDK, "platforms", "android-34", "android.jar")
JDK_BIN = r"C:\Program Files\Microsoft\jdk-21.0.12.101-hotspot\bin"
KEYSTORE = os.path.normpath(os.path.join(ROOT, "..", "cloudredirect.keystore"))

AAPT2 = os.path.join(BUILD_TOOLS, "aapt2.exe")
D8 = os.path.join(BUILD_TOOLS, "d8.bat")
ZIPALIGN = os.path.join(BUILD_TOOLS, "zipalign.exe")
APKSIGNER = os.path.join(BUILD_TOOLS, "apksigner.bat")
JAVAC = os.path.join(JDK_BIN, "javac.exe")

OUTPUT_DIR = os.path.join(ROOT, "build")
if os.path.exists(OUTPUT_DIR):
    shutil.rmtree(OUTPUT_DIR)
os.makedirs(OUTPUT_DIR, exist_ok=True)

GEN_DIR = os.path.join(OUTPUT_DIR, "gen")
CLASSES_DIR = os.path.join(OUTPUT_DIR, "classes")
DEX_DIR = os.path.join(OUTPUT_DIR, "dex")
os.makedirs(GEN_DIR, exist_ok=True)
os.makedirs(CLASSES_DIR, exist_ok=True)
os.makedirs(DEX_DIR, exist_ok=True)

# 1. Compile Resources with aapt2
res_zip = os.path.join(OUTPUT_DIR, "res.zip")
print("1. Compiling resources with aapt2...")
cmd = [AAPT2, "compile", "--dir", os.path.join(ROOT, "res"), "-o", res_zip]
subprocess.check_call(cmd)

# 2. Link with aapt2
print("2. Linking resources with aapt2...")
unaligned_apk = os.path.join(OUTPUT_DIR, "unaligned.apk")
manifest = os.path.join(ROOT, "AndroidManifest.xml")
cmd = [
    AAPT2, "link",
    "-I", PLATFORM_JAR,
    "--manifest", manifest,
    "--java", GEN_DIR,
    "-o", unaligned_apk,
    res_zip
]
subprocess.check_call(cmd)

# 3. Compile Java with javac
print("3. Compiling Java sources with javac...")
src_dir = os.path.join(ROOT, "src", "com", "cloudredirect", "gamehub")
java_files = [os.path.join(src_dir, f) for f in os.listdir(src_dir) if f.endswith(".java")]
r_java = os.path.join(GEN_DIR, "com", "cloudredirect", "gamehub", "R.java")
if os.path.exists(r_java):
    java_files.append(r_java)

cmd = [
    JAVAC,
    "-source", "1.8",
    "-target", "1.8",
    "-cp", PLATFORM_JAR,
    "-d", CLASSES_DIR
] + java_files
subprocess.check_call(cmd)

# 4. Dex with d8
print("4. Dexing classes with d8...")
classes_jar = os.path.join(OUTPUT_DIR, "classes.jar")
with zipfile.ZipFile(classes_jar, 'w') as jf:
    for root, dirs, files in os.walk(CLASSES_DIR):
        for f in files:
            if f.endswith(".class"):
                abs_f = os.path.join(root, f)
                rel_f = os.path.relpath(abs_f, CLASSES_DIR)
                jf.write(abs_f, rel_f)

cmd = f'"{D8}" --lib "{PLATFORM_JAR}" --output "{DEX_DIR}" "{classes_jar}"'
subprocess.check_call(cmd, shell=True)

# 5. Add classes.dex into unaligned.apk
print("5. Packing classes.dex into APK...")
dex_file = os.path.join(DEX_DIR, "classes.dex")
with zipfile.ZipFile(unaligned_apk, 'a') as zf:
    zf.write(dex_file, "classes.dex")

# 6. Zipalign
print("6. Zipaligning APK...")
aligned_apk = os.path.join(OUTPUT_DIR, "aligned.apk")
cmd = [ZIPALIGN, "-f", "-p", "4", unaligned_apk, aligned_apk]
subprocess.check_call(cmd)

# 7. Apksigner
final_apk = os.path.join(ROOT, "..", "GameHub-TouchHUD.apk")
final_apk = os.path.normpath(final_apk)
print(f"7. Signing APK to {final_apk}...")
cmd = [
    APKSIGNER, "sign",
    "--ks", KEYSTORE,
    "--ks-pass", "pass:cloudredirect",
    "--key-pass", "pass:cloudredirect",
    "--out", final_apk,
    aligned_apk
]
subprocess.check_call(cmd, shell=True)

# Also create GameHub-HUD.apk copy
hud_apk = os.path.join(ROOT, "..", "GameHub-HUD.apk")
shutil.copy2(final_apk, hud_apk)

# Copy to publish directory
publish_dir = os.path.normpath(os.path.join(ROOT, "..", "..", "..", "ui", "bin", "publish"))
if os.path.exists(publish_dir):
    shutil.copy2(final_apk, os.path.join(publish_dir, "GameHub-TouchHUD.apk"))
    shutil.copy2(final_apk, os.path.join(publish_dir, "GameHub-HUD.apk"))

print(f"SUCCESS: Created and signed {final_apk} (Size: {os.path.getsize(final_apk)} bytes)")
