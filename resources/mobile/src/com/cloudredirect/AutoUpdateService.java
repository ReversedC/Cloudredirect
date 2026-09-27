package com.cloudredirect;

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
import android.widget.Toast;
import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class AutoUpdateService {
    public static final String APP_VERSION = "2.9.43";
    private static final String PREFS_NAME = "CloudRedirectUpdatePrefs";
    private static final String KEY_LAST_CHECK = "last_check_timestamp";
    private static final String GITHUB_LATEST_RELEASE = 
        "https://api.github.com/repos/mirzaarsyad74-cmyk/Cloudredirect/releases/latest";
    private static final String FALLBACK_DOWNLOAD_URL = 
        "https://github.com/mirzaarsyad74-cmyk/Cloudredirect/releases/latest/download/CloudRedirect-Stream.apk";

    public static void checkOnLaunch(final Activity activity) {
        if (activity == null) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    // Check at most once every 10 minutes on auto-launch
                    SharedPreferences prefs = activity.getSharedPreferences(PREFS_NAME, Context.MODE_PRIVATE);
                    long lastCheck = prefs.getLong(KEY_LAST_CHECK, 0);
                    long now = System.currentTimeMillis();
                    if (now - lastCheck < 600000) {
                        return;
                    }
                    prefs.edit().putLong(KEY_LAST_CHECK, now).apply();
                    performCheck(activity, false);
                } catch (Throwable t) {
                    t.printStackTrace();
                }
            }
        }).start();
    }

    public static void checkManual(final Activity activity) {
        if (activity == null) return;
        Toast.makeText(activity, "Checking for CloudRedirect updates...", Toast.LENGTH_SHORT).show();
        new Thread(new Runnable() {
            @Override
            public void run() {
                performCheck(activity, true);
            }
        }).start();
    }

    private static void performCheck(final Activity activity, final boolean isManual) {
        try {
            URL url = new URL(GITHUB_LATEST_RELEASE);
            HttpURLConnection conn = (HttpURLConnection) url.openConnection();
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "CloudRedirectStream/" + APP_VERSION);
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
                final String releaseBody = release.optString("body", "Performance enhancements and bug fixes.");

                // Find download URL for CloudRedirect-Stream.apk
                String downloadUrl = FALLBACK_DOWNLOAD_URL;
                JSONArray assets = release.optJSONArray("assets");
                if (assets != null) {
                    for (int i = 0; i < assets.length(); i++) {
                        JSONObject asset = assets.getJSONObject(i);
                        if ("CloudRedirect-Stream.apk".equalsIgnoreCase(asset.optString("name"))) {
                            downloadUrl = asset.optString("browser_download_url", FALLBACK_DOWNLOAD_URL);
                            break;
                        }
                    }
                }

                final String finalDownloadUrl = downloadUrl;

                if (isNewerVersion(cleanTag, APP_VERSION)) {
                    activity.runOnUiThread(new Runnable() {
                        @Override
                        public void run() {
                            showUpdatePrompt(activity, cleanTag, releaseBody, finalDownloadUrl);
                        }
                    });
                    return;
                }
            }

            if (isManual) {
                activity.runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        Toast.makeText(activity, "CloudRedirect Stream is up to date! (v" + APP_VERSION + ")", Toast.LENGTH_SHORT).show();
                    }
                });
            }
        } catch (final Throwable t) {
            if (isManual) {
                activity.runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        Toast.makeText(activity, "Could not check updates: " + t.getMessage(), Toast.LENGTH_SHORT).show();
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

    private static void showUpdatePrompt(final Activity activity, final String version, final String notes, final String downloadUrl) {
        if (activity.isFinishing()) return;

        AlertDialog.Builder b = new AlertDialog.Builder(activity, android.R.style.Theme_DeviceDefault_Dialog_Alert);
        b.setTitle("🚀 Update Available: v" + version);
        String msg = "A new version of CloudRedirect Stream is available!\n\n" +
                     "Current Version: v" + APP_VERSION + "\n" +
                     "Latest Version: v" + version + "\n\n" +
                     (notes.length() > 250 ? notes.substring(0, 250) + "..." : notes);
        b.setMessage(msg);
        b.setPositiveButton("Update Now", new DialogInterface.OnClickListener() {
            @Override
            public void onClick(DialogInterface dialog, int which) {
                downloadAndInstall(activity, downloadUrl);
            }
        });
        b.setNegativeButton("Later", null);
        b.show();
    }

    public static void downloadAndInstall(final Activity activity, final String downloadUrl) {
        final Context appContext = activity.getApplicationContext();
        try {
            Toast.makeText(activity, "Downloading CloudRedirect update...", Toast.LENGTH_SHORT).show();

            // On Android 8.0+, check if unknown sources install is allowed
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
                req.setTitle("CloudRedirect-Stream.apk");
                req.setDescription("Downloading CloudRedirect Stream update...");
                req.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                req.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, "CloudRedirect-Stream.apk");
                req.setMimeType("application/vnd.android.package-archive");
                final long downloadId = dm.enqueue(req);

                final BroadcastReceiver onComplete = new BroadcastReceiver() {
                    @Override
                    public void onReceive(Context context, Intent intent) {
                        try {
                            long id = intent.getLongExtra(DownloadManager.EXTRA_DOWNLOAD_ID, -1);
                            if (id == downloadId) {
                                try {
                                    context.getApplicationContext().unregisterReceiver(this);
                                } catch (Throwable ignored) {}

                                DownloadManager d = (DownloadManager) context.getSystemService(Context.DOWNLOAD_SERVICE);
                                Uri fileUri = d.getUriForDownloadedFile(downloadId);
                                if (fileUri != null) {
                                    Intent install = new Intent(Intent.ACTION_VIEW);
                                    install.setDataAndType(fileUri, "application/vnd.android.package-archive");
                                    install.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
                                    install.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
                                    install.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP);
                                    context.startActivity(install);
                                }
                            }
                        } catch (Throwable t) {
                            t.printStackTrace();
                        }
                    }
                };

                // Handle Android 14+ RECEIVER_EXPORTED
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

        // Fallback: Open browser directly to download APK
        try {
            Intent browser = new Intent(Intent.ACTION_VIEW, Uri.parse(downloadUrl));
            browser.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            appContext.startActivity(browser);
        } catch (Throwable t2) {
            Toast.makeText(appContext, "Download error: " + t2.getMessage(), Toast.LENGTH_LONG).show();
        }
    }
}
