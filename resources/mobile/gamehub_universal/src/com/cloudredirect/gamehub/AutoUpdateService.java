package com.cloudredirect.gamehub;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.ProgressDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.provider.Settings;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class AutoUpdateService {
    public static final String APP_VERSION = "2.9.49";
    private static final String PREFS_NAME = "GameHubUpdatePrefs";
    private static final String KEY_LAST_CHECK = "last_check_timestamp";
    private static final String GITHUB_LATEST_RELEASE = 
        "https://api.github.com/repos/mirzaarsyad74-cmyk/Cloudredirect/releases/latest";
    private static final String FALLBACK_DOWNLOAD_URL = 
        "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/latest/download/GameHub-TouchHUD.apk";
    private static final String UPDATE_FILENAME = "GameHub-Update.apk";

    private static final Handler mainHandler = new Handler(Looper.getMainLooper());
    private static boolean isDownloading = false;

    public static void checkOnLaunch(final Context context) {
        if (context == null) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    SharedPreferences prefs = context.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
                    long lastCheck = prefs.getLong(KEY_LAST_CHECK, 0);
                    long now = System.currentTimeMillis();
                    // Don't spam: check at most once every 5 minutes on background launches
                    if (now - lastCheck < 300000) {
                        return;
                    }
                    prefs.edit().putLong(KEY_LAST_CHECK, now).apply();
                    performCheck(context, false);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        }).start();
    }

    public static void checkManual(final Context context) {
        if (context == null) return;
        if (isDownloading) {
            Toast.makeText(context, "An update is already downloading...", Toast.LENGTH_SHORT).show();
            return;
        }
        Toast.makeText(context, "Checking for GameHub updates...", Toast.LENGTH_SHORT).show();
        new Thread(new Runnable() {
            @Override
            public void run() {
                performCheck(context, true);
            }
        }).start();
    }

    public static void checkPendingInstallOnResume(final Activity activity) {
        if (activity == null || isDownloading) return;
        final File updateFile = getUpdateFile(activity);
        if (updateFile != null && updateFile.exists() && updateFile.length() > 20000) {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O || activity.getPackageManager().canRequestPackageInstalls()) {
                mainHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        AlertDialog.Builder b = new AlertDialog.Builder(activity, android.R.style.Theme_DeviceDefault_Dialog_Alert);
                        b.setTitle("🔄 Install GameHub Update");
                        b.setMessage("The latest GameHub update is downloaded and ready to install.");
                        b.setPositiveButton("Install Now", new DialogInterface.OnClickListener() {
                            @Override
                            public void onClick(DialogInterface dialog, int which) {
                                installApk(activity, updateFile);
                            }
                        });
                        b.setNegativeButton("Later", null);
                        b.show();
                    }
                });
            }
        }
    }

    private static void performCheck(final Context context, final boolean isManual) {
        try {
            HttpURLConnection conn = openConnectionWithRedirects(GITHUB_LATEST_RELEASE);
            conn.setRequestProperty("Accept", "application/vnd.github.v3+json");

            if (conn.getResponseCode() == 200) {
                BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    sb.append(line);
                }
                reader.close();
                conn.disconnect();

                JSONObject release = new JSONObject(sb.toString());
                String tagName = release.optString("tag_name", "");
                final String cleanTag = tagName.startsWith("v") ? tagName.substring(1) : tagName;
                final String releaseBody = release.optString("body", "Performance improvements, new touch controls and visual enhancements.");

                // Find download URL for GameHub-TouchHUD.apk or GameHub-HUD.apk
                String downloadUrl = FALLBACK_DOWNLOAD_URL;
                JSONArray assets = release.optJSONArray("assets");
                if (assets != null) {
                    for (int i = 0; i < assets.length(); i++) {
                        JSONObject asset = assets.getJSONObject(i);
                        String name = asset.optString("name");
                        if ("GameHub-TouchHUD.apk".equalsIgnoreCase(name) || "GameHub-HUD.apk".equalsIgnoreCase(name)) {
                            downloadUrl = asset.optString("browser_download_url", FALLBACK_DOWNLOAD_URL);
                            break;
                        }
                    }
                }

                final String finalDownloadUrl = downloadUrl;

                if (isNewerVersion(cleanTag, APP_VERSION)) {
                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            showUpdatePrompt(context, cleanTag, releaseBody, finalDownloadUrl);
                        }
                    });
                    return;
                }
            } else {
                conn.disconnect();
            }

            if (isManual) {
                mainHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        Toast.makeText(context, "GameHub HUD is up to date! (v" + APP_VERSION + ")", Toast.LENGTH_SHORT).show();
                    }
                });
            }
        } catch (final Throwable t) {
            if (isManual) {
                mainHandler.post(new Runnable() {
                    @Override
                    public void run() {
                        Toast.makeText(context, "Could not check updates: " + t.getMessage(), Toast.LENGTH_SHORT).show();
                    }
                });
            }
        }
    }

    private static boolean isNewerVersion(String remote, String local) {
        try {
            String[] rParts = remote.split("\\.");
            String[] lParts = local.split("\\.");
            int len = Math.max(rParts.length, lParts.length);
            for (int i = 0; i < len; i++) {
                int r = i < rParts.length ? Integer.parseInt(rParts[i].replaceAll("[^0-9]", "")) : 0;
                int l = i < lParts.length ? Integer.parseInt(lParts[i].replaceAll("[^0-9]", "")) : 0;
                if (r > l) return true;
                if (r < l) return false;
            }
            return false;
        } catch (Exception e) {
            return false;
        }
    }

    private static void showUpdatePrompt(final Context context, final String newVersion, String body, final String downloadUrl) {
        AlertDialog.Builder b = new AlertDialog.Builder(context, android.R.style.Theme_DeviceDefault_Dialog_Alert);
        b.setTitle("🔄 GameHub Update Available");
        b.setMessage("A new version of GameHub Mobile HUD (v" + newVersion + ") is available!\n\n" +
                     "Current Version: v" + APP_VERSION + "\n\n" +
                     "What's New:\n" + body);
        b.setPositiveButton("Download & Install", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                startDirectDownload(context, downloadUrl, newVersion);
            }
        });
        b.setNegativeButton("Later", null);

        AlertDialog dialog = b.create();
        if (!(context instanceof Activity) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (dialog.getWindow() != null) {
                dialog.getWindow().setType(android.view.WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY);
            }
        }
        dialog.show();
    }

    public static void startDirectDownload(final Context context, final String downloadUrl, final String newVersion) {
        if (isDownloading) {
            Toast.makeText(context, "Download already in progress...", Toast.LENGTH_SHORT).show();
            return;
        }

        // On Android 8+, check unknown source permission ahead of time
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            if (!context.getPackageManager().canRequestPackageInstalls()) {
                AlertDialog.Builder permDialog = new AlertDialog.Builder(context, android.R.style.Theme_DeviceDefault_Dialog_Alert);
                permDialog.setTitle("Allow Seamless Updates");
                permDialog.setMessage("To install GameHub updates automatically without using a browser, please enable 'Allow from this source' for GameHub on the next screen.");
                permDialog.setPositiveButton("Open Settings", new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        try {
                            Intent settingsIntent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
                            settingsIntent.setData(Uri.parse("package:" + context.getPackageName()));
                            settingsIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                            context.startActivity(settingsIntent);
                        } catch (Throwable ignored) {}
                    }
                });
                permDialog.setNegativeButton("Cancel", null);
                permDialog.show();
                // We still proceed with the background download so it's ready as soon as the user returns!
            }
        }

        final ProgressDialog progressDialog;
        if (context instanceof Activity) {
            progressDialog = new ProgressDialog(context, android.R.style.Theme_DeviceDefault_Dialog_Alert);
            progressDialog.setTitle("🔄 Updating GameHub (v" + newVersion + ")");
            progressDialog.setMessage("Connecting to update server...");
            progressDialog.setProgressStyle(ProgressDialog.STYLE_HORIZONTAL);
            progressDialog.setIndeterminate(false);
            progressDialog.setMax(100);
            progressDialog.setProgress(0);
            progressDialog.setCancelable(false);
            progressDialog.show();
        } else {
            progressDialog = null;
            Toast.makeText(context, "Downloading GameHub update in background...", Toast.LENGTH_LONG).show();
        }

        isDownloading = true;

        new Thread(new Runnable() {
            @Override
            public void run() {
                File targetFile = getUpdateFile(context);
                if (targetFile != null && targetFile.exists()) {
                    targetFile.delete();
                }

                InputStream in = null;
                FileOutputStream out = null;
                HttpURLConnection conn = null;

                try {
                    conn = openConnectionWithRedirects(downloadUrl);
                    int responseCode = conn.getResponseCode();
                    if (responseCode != HttpURLConnection.HTTP_OK) {
                        throw new Exception("Server returned HTTP " + responseCode);
                    }

                    final int fileLength = conn.getContentLength();
                    in = conn.getInputStream();
                    out = new FileOutputStream(targetFile);

                    byte[] buffer = new byte[8192];
                    long total = 0;
                    int count;
                    long lastUpdateTime = 0;

                    while ((count = in.read(buffer)) != -1) {
                        total += count;
                        out.write(buffer, 0, count);

                        long now = System.currentTimeMillis();
                        if (fileLength > 0 && progressDialog != null && (now - lastUpdateTime > 80)) {
                            lastUpdateTime = now;
                            final int progressPct = (int) ((total * 100) / fileLength);
                            final long curTotal = total;
                            mainHandler.post(new Runnable() {
                                @Override
                                public void run() {
                                    if (progressDialog.isShowing()) {
                                        progressDialog.setProgress(progressPct);
                                        double currentMb = curTotal / (1024.0 * 1024.0);
                                        double totalMb = fileLength / (1024.0 * 1024.0);
                                        if (totalMb < 1.0) {
                                            progressDialog.setMessage(String.format("Downloaded: %d KB / %d KB (%d%%)", 
                                                    curTotal / 1024, fileLength / 1024, progressPct));
                                        } else {
                                            progressDialog.setMessage(String.format("Downloaded: %.1f MB / %.1f MB (%d%%)", 
                                                    currentMb, totalMb, progressPct));
                                        }
                                    }
                                }
                            });
                        }
                    }

                    out.flush();
                    final File finalFile = targetFile;

                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            isDownloading = false;
                            if (progressDialog != null && progressDialog.isShowing()) {
                                progressDialog.dismiss();
                            }
                            installApk(context, finalFile);
                        }
                    });

                } catch (final Throwable t) {
                    t.printStackTrace();
                    mainHandler.post(new Runnable() {
                        @Override
                        public void run() {
                            isDownloading = false;
                            if (progressDialog != null && progressDialog.isShowing()) {
                                progressDialog.dismiss();
                            }
                            Toast.makeText(context, "Update failed: " + t.getMessage(), Toast.LENGTH_LONG).show();
                        }
                    });
                } finally {
                    try {
                        if (in != null) in.close();
                    } catch (Throwable ignored) {}
                    try {
                        if (out != null) out.close();
                    } catch (Throwable ignored) {}
                    if (conn != null) {
                        try {
                            conn.disconnect();
                        } catch (Throwable ignored) {}
                    }
                }
            }
        }).start();
    }

    public static void installApk(Context context, File apkFile) {
        if (apkFile == null || !apkFile.exists() || apkFile.length() < 20000) {
            Toast.makeText(context, "Invalid update package", Toast.LENGTH_SHORT).show();
            return;
        }

        try {
            Uri apkUri = GameHubFileProvider.getUriForFile(context, apkFile);
            Intent installIntent = new Intent(Intent.ACTION_VIEW);
            installIntent.setDataAndType(apkUri, "application/vnd.android.package-archive");
            installIntent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            installIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            installIntent.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
            context.startActivity(installIntent);
        } catch (Throwable t) {
            t.printStackTrace();
            Toast.makeText(context, "Could not launch package installer: " + t.getMessage(), Toast.LENGTH_LONG).show();
        }
    }

    private static File getUpdateFile(Context context) {
        File dir = context.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
        if (dir == null || !dir.canWrite()) {
            dir = context.getCacheDir();
        }
        return new File(dir, UPDATE_FILENAME);
    }

    private static HttpURLConnection openConnectionWithRedirects(String urlString) throws Exception {
        int redirects = 0;
        while (redirects < 8) {
            URL url = new URL(urlString);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setInstanceFollowRedirects(true);
            conn.setRequestProperty("User-Agent", "GameHubMobileHUD/" + APP_VERSION);
            conn.setConnectTimeout(12000);
            conn.setReadTimeout(20000);

            int status = conn.getResponseCode();
            if (status == HttpURLConnection.HTTP_MOVED_TEMP || 
                status == HttpURLConnection.HTTP_MOVED_PERM || 
                status == 307 || status == 308) {
                String newUrl = conn.getHeaderField("Location");
                if (newUrl != null && !newUrl.isEmpty()) {
                    urlString = newUrl;
                    conn.disconnect();
                    redirects++;
                    continue;
                }
            }
            return conn;
        }
        throw new Exception("Too many HTTP redirects");
    }
}
