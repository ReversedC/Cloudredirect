package com.cloudredirect.suolink;

import java.net.HttpURLConnection;
import java.net.URL;

public class SmartConnectRunnable implements Runnable {
    private final MainActivity activity;
    private final String lanUrl;
    private final String tunnelUrl;

    public SmartConnectRunnable(MainActivity activity, String lanUrl, String tunnelUrl) {
        this.activity = activity;
        this.lanUrl = lanUrl;
        this.tunnelUrl = tunnelUrl;
    }

    @Override
    public void run() {
        if (activity == null) return;

        // If only LAN URL exists, load it immediately without delaying
        if (tunnelUrl == null || tunnelUrl.isEmpty()) {
            if (lanUrl != null && !lanUrl.isEmpty()) {
                activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
            }
            return;
        }

        // If only tunnel exists, load it immediately
        if (lanUrl == null || lanUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tunnelUrl));
            return;
        }

        // Both exist: fast probe LAN (800ms)
        boolean lanSuccess = false;
        HttpURLConnection conn = null;
        try {
            String probeUrl = lanUrl;
            int qIdx = probeUrl.indexOf('?');
            if (qIdx > 0) probeUrl = probeUrl.substring(0, qIdx);
            if (probeUrl.endsWith("/")) probeUrl = probeUrl.substring(0, probeUrl.length() - 1);
            probeUrl += "/api/status";

            URL url = new URL(probeUrl);
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(800);
            conn.setReadTimeout(800);
            conn.setRequestMethod("GET");

            if (conn.getResponseCode() == 200) {
                lanSuccess = true;
            }
        } catch (Exception ignored) {
            lanSuccess = false;
        } finally {
            if (conn != null) {
                try { conn.disconnect(); } catch (Exception ignored) {}
            }
        }

        if (lanSuccess) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
            activity.runOnUiThread(new ShowToastRunnable(activity, "Connected via Local Wi-Fi"));
        } else {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tunnelUrl));
            activity.runOnUiThread(new ShowToastRunnable(activity, "Connected via Remote Tunnel (Different Network)"));
        }
    }
}
