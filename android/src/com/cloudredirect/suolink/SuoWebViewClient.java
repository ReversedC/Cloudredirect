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
    private boolean hasTriedLanFallback = false;

    public SuoWebViewClient(MainActivity activity) {
        this.activity = activity;
    }

    public void resetFallbacks() {
        hasTriedTunnelFallback = false;
        hasTriedLanFallback = false;
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
            hasTriedTunnelFallback = false;
            hasTriedLanFallback = false;
        }
    }

    @Override
    public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
        if (request != null && request.getUrl() != null) {
            String host = request.getUrl().getHost();
            if ("suolink.local".equalsIgnoreCase(host)) {
                return new WebResourceResponse("text/plain", "UTF-8", new java.io.ByteArrayInputStream(new byte[0]));
            }
        }
        return super.shouldInterceptRequest(view, request);
    }

    @Override
    public WebResourceResponse shouldInterceptRequest(WebView view, String url) {
        if (url != null && url.contains("suolink.local")) {
            return new WebResourceResponse("text/plain", "UTF-8", new java.io.ByteArrayInputStream(new byte[0]));
        }
        return super.shouldInterceptRequest(view, url);
    }

    private boolean isHostUrl(String url) {
        if (url == null || url.trim().isEmpty()) return false;
        String lower = url.trim().toLowerCase();
        if (lower.startsWith("file://") || lower.startsWith("data:") || lower.startsWith("about:") || lower.contains("suolink.local")) {
            return false;
        }
        return lower.startsWith("http://") || lower.startsWith("https://");
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
        if (!isHostUrl(failingUrl)) return;
        handleConnectionFailure(failingUrl, description);
    }

    @Override
    public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
        super.onReceivedError(view, request, error);
        if (request != null && request.isForMainFrame()) {
            String url = request.getUrl() != null ? request.getUrl().toString() : "";
            if (!isHostUrl(url)) return;
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
                if (!isHostUrl(url)) return;
                handleConnectionFailure(url, "HTTP " + statusCode + " - Tunnel gateway error");
            }
        }
    }

    private void handleConnectionFailure(String failingUrl, String desc) {
        if (!isHostUrl(failingUrl)) return;

        // 1. If LAN failed, check if we have a remote tunnel available to auto-switch!
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

        // 2. If Remote Tunnel failed (e.g. trycloudflare DNS blocked by ISP), check if we have LAN available!
        if (!hasTriedLanFallback) {
            String lan = activity.getSavedLanUrl();
            if (lan != null && !lan.isEmpty() && failingUrl != null 
                    && (failingUrl.contains("trycloudflare") || failingUrl.startsWith("https://"))) {
                hasTriedLanFallback = true;
                activity.runOnUiThread(new LoadUrlRunnable(activity, lan));
                activity.runOnUiThread(new ShowToastRunnable(activity, "Remote tunnel unreachable. Auto-switched to Local Wi-Fi!"));
                return;
            }
        }

        activity.showError(failingUrl, desc);
    }
}
