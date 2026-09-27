package com.cloudredirect.suolink;

import org.json.JSONArray;
import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class CheckUpdateRunnable implements Runnable {
    private final MainActivity activity;
    private final String optionalHost;

    public CheckUpdateRunnable(MainActivity activity) {
        this(activity, null);
    }

    public CheckUpdateRunnable(MainActivity activity, String optionalHost) {
        this.activity = activity;
        this.optionalHost = optionalHost;
    }

    public static int parseVersionCode(String ver) {
        if (ver == null) return 0;
        ver = ver.trim();
        while (ver.startsWith("v") || ver.startsWith("V")) ver = ver.substring(1);
        String[] parts = ver.split("\\.");
        try {
            int major = parts.length > 0 ? Integer.parseInt(parts[0].replaceAll("[^0-9]", "")) : 0;
            int minor = parts.length > 1 ? Integer.parseInt(parts[1].replaceAll("[^0-9]", "")) : 0;
            int patch = parts.length > 2 ? Integer.parseInt(parts[2].replaceAll("[^0-9]", "")) : 0;
            return major * 10000 + minor * 100 + patch;
        } catch (Exception ex) {
            return 0;
        }
    }

    public static String cleanVersion(String ver) {
        if (ver == null) return "";
        ver = ver.trim();
        while (ver.startsWith("v") || ver.startsWith("V")) ver = ver.substring(1);
        return ver;
    }

    @Override
    public void run() {
        int localVersionCode = 1;
        String localVersionName = "1.0.0";
        try {
            localVersionCode = activity.getPackageManager()
                    .getPackageInfo(activity.getPackageName(), 0).versionCode;
            localVersionName = activity.getPackageManager()
                    .getPackageInfo(activity.getPackageName(), 0).versionName;
        } catch (Exception ignored) { }

        // If localVersionCode was small (e.g. legacy 1, 2, 3), parse from versionName
        int parsedLocalCode = parseVersionCode(localVersionName);
        if (parsedLocalCode > localVersionCode) {
            localVersionCode = parsedLocalCode;
        }

        // 1. Try checking specified host or saved hosts
        String[] hostCandidates = getHostCandidates();
        for (String host : hostCandidates) {
            if (host == null || host.isEmpty()) continue;
            if (checkHostVersion(host, localVersionCode)) {
                return; // Successfully checked and handled
            }
        }

        // 2. Fallback: Check GitHub Releases API if host not reachable
        checkGitHubReleases(localVersionCode);
    }

    private String[] getHostCandidates() {
        if (optionalHost != null && !optionalHost.isEmpty()) {
            return new String[]{ optionalHost };
        }
        String lan = activity.getSavedLanUrl();
        String tunnel = activity.getSavedTunnelUrl();
        return new String[]{ lan, tunnel };
    }

    private boolean checkHostVersion(String baseHostUrl, int localVersionCode) {
        HttpURLConnection conn = null;
        try {
            String baseUrl = baseHostUrl;
            int qIdx = baseUrl.indexOf('?');
            if (qIdx > 0) baseUrl = baseUrl.substring(0, qIdx);
            if (baseUrl.endsWith("/")) baseUrl = baseUrl.substring(0, baseUrl.length() - 1);

            URL url = new URL(baseUrl + "/api/version");
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(2500);
            conn.setReadTimeout(2500);
            conn.setRequestMethod("GET");

            if (conn.getResponseCode() == 200) {
                BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    sb.append(line);
                }
                reader.close();

                JSONObject json = new JSONObject(sb.toString());
                int remoteVersionCode = json.optInt("versionCode", 1);
                String remoteVersion = cleanVersion(json.optString("version", "Latest"));
                int parsedRemoteCode = parseVersionCode(remoteVersion);
                if (parsedRemoteCode > remoteVersionCode) remoteVersionCode = parsedRemoteCode;

                String apkPath = json.optString("apkUrl", "/download/suo-link.apk");
                String fullApkUrl = baseUrl + apkPath;

                // ONLY trigger update if strictly greater than local version!
                if (remoteVersionCode > localVersionCode) {
                    activity.runOnUiThread(new OnUpdateFoundRunnable(activity, remoteVersion, fullApkUrl));
                }
                return true;
            }
        } catch (Exception ignored) {
        } finally {
            if (conn != null) conn.disconnect();
        }
        return false;
    }

    private void checkGitHubReleases(int localVersionCode) {
        HttpURLConnection conn = null;
        try {
            URL url = new URL("https://api.github.com/repos/mirzaarsyad74-cmyk/Cloudredirect/releases/latest");
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(4000);
            conn.setReadTimeout(4000);
            conn.setRequestMethod("GET");
            conn.setRequestProperty("User-Agent", "SUO-Link-Android");

            if (conn.getResponseCode() == 200) {
                BufferedReader reader = new BufferedReader(new InputStreamReader(conn.getInputStream()));
                StringBuilder sb = new StringBuilder();
                String line;
                while ((line = reader.readLine()) != null) {
                    sb.append(line);
                }
                reader.close();

                JSONObject release = new JSONObject(sb.toString());
                String tagName = release.optString("tag_name", ""); // e.g. "v2.9.22"
                String cleanRemoteVer = cleanVersion(tagName);
                int remoteCode = parseVersionCode(cleanRemoteVer);

                // If remote is NOT newer than local, DO NOTHING!
                if (remoteCode <= localVersionCode) {
                    return;
                }

                JSONArray assets = release.optJSONArray("assets");
                if (assets != null) {
                    for (int i = 0; i < assets.length(); i++) {
                        JSONObject asset = assets.getJSONObject(i);
                        String name = asset.optString("name", "");
                        if ("SUO-Link.apk".equalsIgnoreCase(name)) {
                            String downloadUrl = asset.optString("browser_download_url", "");
                            if (!downloadUrl.isEmpty()) {
                                activity.runOnUiThread(new OnUpdateFoundRunnable(activity, cleanRemoteVer, downloadUrl));
                                return;
                            }
                        }
                    }
                }
            }
        } catch (Exception ignored) {
        } finally {
            if (conn != null) conn.disconnect();
        }
    }
}
