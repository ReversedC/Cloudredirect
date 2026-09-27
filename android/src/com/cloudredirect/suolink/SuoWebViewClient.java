package com.cloudredirect.suolink;

import android.net.http.SslError;
import android.os.Build;
import android.webkit.SslErrorHandler;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import android.webkit.WebViewClient;

public class SuoWebViewClient extends WebViewClient {
    private final MainActivity activity;
    private boolean hasTriedTunnelFallback = false;

    public SuoWebViewClient(MainActivity activity) {
        this.activity = activity;
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, String url) {
        if (url != null) {
            if (url.startsWith("suolink://scan")) {
                activity.runOnUiThread(new OpenCameraRunnable(activity));
                return true;
            }
            if (url.startsWith("suolink://gallery")) {
                activity.runOnUiThread(new OpenGalleryRunnable(activity));
                return true;
            }
            if (url.startsWith("suolink://discovery")) {
                activity.runOnUiThread(new ShowDiscoveryRunnable(activity));
                return true;
            }
        }
        return super.shouldOverrideUrlLoading(view, url);
    }

    @Override
    public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
        if (request != null && request.getUrl() != null) {
            return shouldOverrideUrlLoading(view, request.getUrl().toString());
        }
        return super.shouldOverrideUrlLoading(view, request);
    }

    @Override
    public void onPageFinished(WebView view, String url) {
        super.onPageFinished(view, url);
        if (url != null && !url.equals("about:blank") 
                && !url.startsWith("data:") 
                && !url.startsWith("file://")
                && !url.contains("suolink.local") 
                && !url.contains("localhost")) {
            activity.setConnected(true);
            hasTriedTunnelFallback = false; // Reset on successful connection
        }
    }

    @Override
    public void onReceivedSslError(WebView view, SslErrorHandler handler, SslError error) {
        // For trycloudflare.com tunnel URLs, proceed through SSL
        String url = error.getUrl();
        if (url != null && url.contains("trycloudflare.com")) {
            handler.proceed();
            return;
        }
        // For local network connections, proceed
        if (url != null && (url.contains("192.168.") || url.contains("10.") || url.contains("172."))) {
            handler.proceed();
            return;
        }
        super.onReceivedSslError(view, handler, error);
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

    @Override
    public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse errorResponse) {
        super.onReceivedHttpError(view, request, errorResponse);
        if (request != null && request.isForMainFrame() && errorResponse != null) {
            int statusCode = errorResponse.getStatusCode();
            if (statusCode == 502 || statusCode == 503 || statusCode == 504) {
                String url = request.getUrl() != null ? request.getUrl().toString() : "";
                handleConnectionFailure(url, "HTTP " + statusCode + " - Tunnel gateway error");
            }
        }
    }

    private void handleConnectionFailure(String failingUrl, String desc) {
        // If LAN failed, check if we have a remote tunnel available to auto-switch!
        if (!hasTriedTunnelFallback) {
            String tunnel = activity.getSavedTunnelUrl();
            if (tunnel != null && !tunnel.isEmpty() && failingUrl != null 
                    && !failingUrl.startsWith("http://127.0.0.1") 
                    && !failingUrl.contains("trycloudflare")) {
                hasTriedTunnelFallback = true;
                activity.runOnUiThread(new LoadUrlRunnable(activity, tunnel));
                activity.runOnUiThread(new ShowToastRunnable(activity, "LAN unreachable. Auto-switched to Remote Tunnel!"));
                return;
            }
        }
        activity.showError(failingUrl, desc);
    }
}
