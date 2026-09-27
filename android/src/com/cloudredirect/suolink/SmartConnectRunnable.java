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
        boolean lanSuccess = false;

        if (lanUrl != null && !lanUrl.isEmpty()) {
            HttpURLConnection conn = null;
            try {
                // Extract base endpoint for health probe
                String probeUrl = lanUrl;
                int qIdx = probeUrl.indexOf('?');
                if (qIdx > 0) probeUrl = probeUrl.substring(0, qIdx);
                if (probeUrl.endsWith("/")) probeUrl = probeUrl.substring(0, probeUrl.length() - 1);
                probeUrl += "/api/status";

                URL url = new URL(probeUrl);
                conn = (HttpURLConnection) url.openConnection();
                conn.setConnectTimeout(1200);
                conn.setReadTimeout(1200);
                conn.setRequestMethod("GET");

                if (conn.getResponseCode() == 200) {
                    lanSuccess = true;
                }
            } catch (Exception ignored) {
                lanSuccess = false;
            } finally {
                if (conn != null) conn.disconnect();
            }
        }

        if (lanSuccess) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
            activity.runOnUiThread(new ShowToastRunnable(activity, "Connected via Local Wi-Fi"));
        } else if (tunnelUrl != null && !tunnelUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tunnelUrl));
            activity.runOnUiThread(new ShowToastRunnable(activity, "Connected via Remote Tunnel (Different Network)"));
        } else if (lanUrl != null && !lanUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
        } else {
            activity.runOnUiThread(new ShowDiscoveryRunnable(activity));
        }
    }
}
