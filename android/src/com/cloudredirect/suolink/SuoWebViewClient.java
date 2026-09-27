package com.cloudredirect.suolink;

import android.os.Build;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebView;
import android.webkit.WebViewClient;

public class SuoWebViewClient extends WebViewClient {
    private final MainActivity activity;

    public SuoWebViewClient(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        if (url != null && !url.equals("about:blank") && !url.startsWith("data:")) {
            activity.setConnected(true);
        }
    }

    @Override
    public void onReceivedError(WebView view, int errorCode, String description, String failingUrl) {
        super.onReceivedError(view, errorCode, description, failingUrl);
        handleConnectionFailure(failingUrl, description);
    }

    @Override
    public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
        super.onReceivedError(view, request, error);
        if (request != null && request.isForMainFrame()) {
            String url = request.getUrl() != null ? request.getUrl().toString() : "";
            String desc = (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && error != null) 
                ? String.valueOf(error.getDescription()) : "Connection failed";
            handleConnectionFailure(url, desc);
        }
    }

    private void handleConnectionFailure(String failingUrl, String desc) {
        // If LAN failed, check if we have a remote tunnel available to auto-switch!
        String tunnel = activity.getSavedTunnelUrl();
        if (tunnel != null && !tunnel.isEmpty() && failingUrl != null && !failingUrl.startsWith("http://127.0.0.1") && !failingUrl.contains("trycloudflare")) {
            activity.runOnUiThread(new LoadUrlRunnable(activity, tunnel));
            activity.runOnUiThread(new ShowToastRunnable(activity, "LAN unreachable. Auto-switched to Remote Tunnel!"));
            return;
        }
        activity.showError(failingUrl, desc);
    }
}
