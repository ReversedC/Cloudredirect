package com.cloudredirect.gamehub;

import android.app.Activity;
import android.app.AlertDialog;
import android.app.DownloadManager;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.os.Handler;
import android.os.Looper;
import android.widget.Toast;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class AutoUpdateService {
    public static final String APP_VERSION = "2.9.48";
    private static final String PREFS_NAME = "GameHubUpdatePrefs";
    private static final String KEY_LAST_CHECK = "last_check_timestamp";
    private static final String GITHUB_LATEST_RELEASE = 
        "https://api.github.com/repos/mirzaarsyad74-cmyk/Cloudredirect/releases/latest";
    private static final String FALLBACK_DOWNLOAD_URL = 
        "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/latest/download/GameHub-TouchHUD.apk";

    private static final Handler mainHandler = new Handler(Looper.getMainLooper());

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
        Toast.makeText(context, "Checking for GameHub updates...", Toast.LENGTH_SHORT).show();
        new Thread(new Runnable() {
            @Override
            public void run() {
                performCheck(context, true);
            }
        }).start();
    }

    private static void performCheck(final Context context, final boolean isManual) {
        try {
            URL url = new URL(GITHUB_LATEST_RELEASE);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "GameHubMobileHUD/" + APP_VERSION);
            conn.setRequestProperty("Accept", "application/vnd.github.v3+json");
            conn.setConnectTimeout(8000);
            conn.setReadTimeout(8000);

            if (conn.getResponseCode() == 200) {
                BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    sb.append(line);
                }
                reader.close();

                JSONObject release = new JSONObject(sb.toString());
                String tagName = release.optString("tag_name", "");
                final String cleanTag = tagName.startsWith("v") ? tagName.substring(1) : tagName;
                final String releaseBody = release.optString("body", "Performance improvements, new touch buttons and controls.");

                // Find download URL for GameHub-TouchHUD.apk (or GameHub-HUD.apk)
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
        b.setTitle("🔄 GameHub HUD Update Available");
        b.setMessage("A new version of GameHub Mobile HUD (v" + newVersion + ") is available!\n\nCurrent Version: v" + APP_VERSION +
                     "\n\nWhat's New:\n" + body);
        b.setPositiveButton("Download & Install", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                startDownloadAndInstall(context, downloadUrl);
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

    private static void startDownloadAndInstall(final Context context, final String downloadUrl) {
        final Context appContext = context.getApplicationContext();
        Toast.makeText(appContext, "Downloading GameHub-TouchHUD.apk update...", Toast.LENGTH_LONG).show();

        try {
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                if (!appContext.getPackageManager().canRequestPackageInstalls()) {
                    try {
                        Intent settingsIntent = new Intent(android.provider.Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES);
                        settingsIntent.setData(Uri.parse("package:" + appContext.getPackageName()));
                        settingsIntent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                        appContext.startActivity(settingsIntent);
                        Toast.makeText(appContext, "Please enable 'Allow from this source' to install updates", Toast.LENGTH_LONG).show();
                    } catch (Throwable ignored) {}
                }
            }

            final DownloadManager dm = (DownloadManager) appContext.getSystemService(Context.DOWNLOAD_SERVICE);
            if (dm != null) {
                DownloadManager.Request req = new DownloadManager.Request(Uri.parse(downloadUrl));
                req.setTitle("GameHub-TouchHUD.apk");
                req.setDescription("Downloading GameHub Mobile HUD update...");
                req.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                req.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, "GameHub-TouchHUD.apk");
                req.setMimeType("application/vnd.android.package-archive");
                final long downloadId = dm.enqueue(req);

                final BroadcastReceiver onComplete = new BroadcastReceiver() {
                    @Override
                    public void onReceive(Context ctx, Intent intent) {
                        try {
                            long id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1);
                            if (id == downloadId) {
                                try {
                                    ctx.getApplicationContext().unregisterReceiver(this);
                                } catch (Throwable ignored) {}

                                DownloadManager d = (DownloadManager) ctx.getSystemService(Context.DOWNLOAD_SERVICE);
                                Uri fileUri = d.getUriForDownloadedFile(downloadId);
                                if (fileUri != null) {
                                    Intent install = new Intent(Intent.ACTION_VIEW);
                                    install.setDataAndType(fileUri, "application/vnd.android.package-archive");
                                    install.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                                    install.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                                    install.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
                                    ctx.startActivity(install);
                                }
                            }
                        } catch (Throwable t) {
                            t.printStackTrace();
                        }
                    }
                };

                if (Build.VERSION.SDK_INT >= 33) {
                    appContext.registerReceiver(onComplete, new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE), 2 /* Context.RECEIVER_EXPORTED */);
                } else {
                    appContext.registerReceiver(onComplete, new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE));
                }
                return;
            }
        } catch (Throwable t) {
            t.printStackTrace();
        }

        // Fallback: Open browser directly
        try {
            Intent browser = new Intent(Intent.ACTION_VIEW, Uri.parse(downloadUrl));
            browser.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            appContext.startActivity(browser);
        } catch (Throwable t2) {
            Toast.makeText(appContext, "Download error: " + t2.getMessage(), Toast.LENGTH_LONG).show();
        }
    }
}
