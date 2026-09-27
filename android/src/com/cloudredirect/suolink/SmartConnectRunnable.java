package com.cloudredirect.suolink;

import java.net.HttpURLConnection;
import java.net.URL;

public class SmartConnectRunnable implements Runnable {
    private final MainActivity activity;
    private final String lanUrl;
    private final String tailscaleUrl;
    private final String tunnelUrl;

    public SmartConnectRunnable(MainActivity activity, String lanUrl, String tailscaleUrl, String tunnelUrl) {
        this.activity = activity;
        this.lanUrl = lanUrl;
        this.tailscaleUrl = tailscaleUrl;
        this.tunnelUrl = tunnelUrl;
    }

    public SmartConnectRunnable(MainActivity activity, String lanUrl, String tunnelUrl) {
        this(activity, lanUrl, null, tunnelUrl);
    }

    private boolean probe(String endpointUrl, int timeoutMs) {
        if (endpointUrl == null || endpointUrl.trim().isEmpty()) return false;
        HttpURLConnection conn = null;
        try {
            String probeUrl = endpointUrl.trim();
            int qIdx = probeUrl.indexOf('?');
            if (qIdx > 0) probeUrl = probeUrl.substring(0, qIdx);
            if (probeUrl.endsWith("/")) probeUrl = probeUrl.substring(0, probeUrl.length() - 1);
            probeUrl += "/api/status";

            URL url = new URL(probeUrl);
            conn = (HttpURLConnection) url.openConnection();
            conn.setConnectTimeout(timeoutMs);
            conn.setReadTimeout(timeoutMs);
            conn.setRequestMethod("GET");
            return (conn.getResponseCode() == 200);
        } catch (Exception ignored) {
            return false;
        } finally {
            if (conn != null) {
                try { conn.disconnect(); } catch (Exception ignored) {}
            }
        }
    }

    @Override
    public void run() {
        if (activity == null) return;

        // 1. If LAN URL is provided, probe it (1200ms timeout for ultra-low latency Wi-Fi)
        if (lanUrl != null && !lanUrl.isEmpty()) {
            if (probe(lanUrl, 1200)) {
                activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
                activity.runOnUiThread(new ShowToastRunnable(activity, "⚡ Connected via Local Wi-Fi (<1ms)"));
                return;
            }
        }

        // 2. If Tailscale WireGuard is provided, probe it (1500ms timeout for direct P2P VPN)
        if (tailscaleUrl != null && !tailscaleUrl.isEmpty()) {
            if (probe(tailscaleUrl, 1500)) {
                activity.runOnUiThread(new LoadUrlRunnable(activity, tailscaleUrl));
                activity.runOnUiThread(new ShowToastRunnable(activity, "🛡️ Connected via Tailscale WireGuard (Direct P2P)"));
                return;
            }
        }

        // 3. If neither LAN nor Tailscale connected, fall back to Remote Tunnel (Cloudflare)
        if (tunnelUrl != null && !tunnelUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tunnelUrl));
            activity.runOnUiThread(new ShowToastRunnable(activity, "🌐 Connected via Remote Tunnel"));
            return;
        }

        // 4. If only Tailscale was configured but probe failed (load anyway so WebView/WebViewClient can show diagnostic)
        if (tailscaleUrl != null && !tailscaleUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tailscaleUrl));
            return;
        }

        // 5. If only LAN was configured but probe failed
        if (lanUrl != null && !lanUrl.isEmpty()) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, lanUrl));
            return;
        }

        activity.runOnUiThread(new ShowDiscoveryRunnable(activity));
    }
}
