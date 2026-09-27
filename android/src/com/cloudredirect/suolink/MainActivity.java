package com.cloudredirect.suolink;

import android.Manifest;
import android.app.Activity;
import android.app.DownloadManager;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.os.Vibrator;
import android.view.InputDevice;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.Window;
import android.view.WindowManager;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.widget.FrameLayout;

public class MainActivity extends Activity {

    private static final String PREFS_NAME = "suo_link_prefs";
    private static final String PREF_KEY_LAN_URL = "last_lan_url";
    private static final String PREF_KEY_TUNNEL_URL = "last_tunnel_url";

    private WebView webView;
    private Vibrator vibrator;
    private SharedPreferences prefs;
    private boolean connected = false;
    private Thread beaconListenerThread;
    private volatile boolean running = true;
    private DownloadCompleteReceiver downloadReceiver;

    public boolean isConnected() { return connected; }
    public void setConnected(boolean val) { this.connected = val; }
    public boolean isRunning() { return running; }

    public String getSavedLanUrl() {
        return prefs != null ? prefs.getString(PREF_KEY_LAN_URL, null) : null;
    }

    public String getSavedTunnelUrl() {
        return prefs != null ? prefs.getString(PREF_KEY_TUNNEL_URL, null) : null;
    }

    public void saveUrls(String lan, String tunnel) {
        if (prefs != null) {
            SharedPreferences.Editor ed = prefs.edit();
            if (lan != null && !lan.isEmpty()) ed.putString(PREF_KEY_LAN_URL, lan);
            if (tunnel != null && !tunnel.isEmpty()) ed.putString(PREF_KEY_TUNNEL_URL, tunnel);
            ed.apply();
        }
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        requestWindowFeature(Window.FEATURE_NO_TITLE);
        getWindow().setFlags(WindowManager.LayoutParams.FLAG_FULLSCREEN,
                WindowManager.LayoutParams.FLAG_FULLSCREEN);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);

        setImmersiveMode();

        prefs = getSharedPreferences(PREFS_NAME, MODE_PRIVATE);
        vibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);

        // Request camera permission for instant QR scanning
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
            if (checkSelfPermission(Manifest.permission.CAMERA) != PackageManager.PERMISSION_GRANTED) {
                requestPermissions(new String[]{Manifest.permission.CAMERA}, 101);
            }
        }

        // Register download receiver for auto-update
        try {
            downloadReceiver = new DownloadCompleteReceiver();
            IntentFilter filter = new IntentFilter(DownloadManager.ACTION_DOWNLOAD_COMPLETE);
            if (Build.VERSION.SDK_INT >= 33) {
                registerReceiver(downloadReceiver, filter, Context.RECEIVER_EXPORTED);
            } else {
                registerReceiver(downloadReceiver, filter);
            }
        } catch (Exception ignored) { }

        FrameLayout rootLayout = new FrameLayout(this);
        rootLayout.setBackgroundColor(0xFF0B0E14);

        webView = new WebView(this);
        webView.setBackgroundColor(0xFF0B0E14);
        configureWebView(webView);

        rootLayout.addView(webView, new FrameLayout.LayoutParams(
                FrameLayout.LayoutParams.MATCH_PARENT,
                FrameLayout.LayoutParams.MATCH_PARENT));

        setContentView(rootLayout);

        // Auto-update check on every app launch
        new Thread(new CheckUpdateRunnable(this)).start();

        // Check if we have saved connection endpoints
        String savedLan = getSavedLanUrl();
        String savedTunnel = getSavedTunnelUrl();

        if ((savedLan != null && !savedLan.isEmpty()) || (savedTunnel != null && !savedTunnel.isEmpty())) {
            new Thread(new SmartConnectRunnable(this, savedLan, savedTunnel)).start();
        } else {
            showDiscoveryPage();
        }

        startBeaconDiscovery();
    }

    private void configureWebView(WebView wv) {
        WebSettings ws = wv.getSettings();
        ws.setJavaScriptEnabled(true);
        ws.setDomStorageEnabled(true);
        ws.setDatabaseEnabled(true);
        ws.setMediaPlaybackRequiresUserGesture(false);
        ws.setCacheMode(WebSettings.LOAD_NO_CACHE);
        ws.setAllowFileAccess(true);
        ws.setAllowContentAccess(true);
        ws.setUseWideViewPort(true);
        ws.setLoadWithOverviewMode(true);

        wv.setWebChromeClient(new SuoWebChromeClient());
        wv.setWebViewClient(new SuoWebViewClient(this));
        wv.addJavascriptInterface(new SuoNativeBridge(this, vibrator), "SuoNative");
    }

    public void showDiscoveryPage() {
        connected = false;
        String html = DiscoveryHtml.getHtml(getSavedLanUrl(), getSavedTunnelUrl());
        webView.loadDataWithBaseURL("file:///android_asset/", html, "text/html", "UTF-8", null);
    }

    public void showError(String failingUrl, String desc) {
        connected = false;
        String html = DiscoveryHtml.getErrorHtml(failingUrl, desc);
        webView.loadDataWithBaseURL(null, html, "text/html", "UTF-8", null);
    }

    public void connectSmart(String inputUrl) {
        if (inputUrl == null || inputUrl.isEmpty()) return;

        String lanUrl = inputUrl;
        String tunnelUrl = null;

        // Parse smart URL: e.g. http://192.168.1.19:8585/?auth=abc&tunnel=https%3A%2F%2Fxxx.trycloudflare.com
        if (inputUrl.contains("tunnel=")) {
            try {
                Uri parsed = Uri.parse(inputUrl);
                String extractedTunnel = parsed.getQueryParameter("tunnel");
                if (extractedTunnel != null && !extractedTunnel.isEmpty()) {
                    tunnelUrl = extractedTunnel;
                }
                // Strip the tunnel query param from the direct LAN URL
                lanUrl = inputUrl.replaceAll("[?&]tunnel=[^&]*", "");
            } catch (Exception ignored) { }
        } else if (inputUrl.contains("trycloudflare.com")) {
            tunnelUrl = inputUrl;
            lanUrl = null;
        }

        saveUrls(lanUrl, tunnelUrl);
        new Thread(new SmartConnectRunnable(this, lanUrl, tunnelUrl)).start();
    }

    public void loadHostUrl(String url) {
        if (url == null || url.isEmpty()) return;
        saveUrls(url, null);
        webView.loadUrl(url);
    }

    public void onBeaconReceived(String name, String ip, int port, String auth, String tunnel, int verCode, String ver) {
        // Feed discovered host into discovery HTML
        String js = String.format("if(window.onHostDiscovered){ window.onHostDiscovered('%s','%s',%d,'%s','%s'); }",
                name, ip, port, auth, tunnel);
        webView.post(new EvalJsRunnable(webView, js));

        // If not connected and discovery page is open, auto connect
        if (!connected) {
            String smartUrl = "http://" + ip + ":" + port + "/?auth=" + auth;
            if (tunnel != null && !tunnel.isEmpty()) {
                smartUrl += "&tunnel=" + Uri.encode(tunnel);
            }
            connectSmart(smartUrl);
        }

        // Trigger update check if beacon reports newer version
        int localVerCode = 1;
        try {
            localVerCode = getPackageManager().getPackageInfo(getPackageName(), 0).versionCode;
        } catch (Exception ignored) { }

        if (verCode > localVerCode) {
            String apkUrl = "http://" + ip + ":" + port + "/download/suo-link.apk";
            onUpdateAvailable(ver, apkUrl);
        }
    }

    public void onUpdateAvailable(String version, String apkDownloadUrl) {
        if (apkDownloadUrl == null || apkDownloadUrl.isEmpty()) return;
        try {
            // Show in-app banner
            String js = "if(window.showUpdateNotice){ window.showUpdateNotice('⬆️ Downloading SUO Link v" + version + "...'); }";
            webView.post(new EvalJsRunnable(webView, js));

            // Download APK via system DownloadManager
            DownloadManager dm = (DownloadManager) getSystemService(Context.DOWNLOAD_SERVICE);
            if (dm != null) {
                DownloadManager.Request req = new DownloadManager.Request(Uri.parse(apkDownloadUrl));
                req.setTitle("SUO Link Update (" + version + ")");
                req.setDescription("Downloading latest SUO Link...");
                req.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                req.setDestinationInExternalFilesDir(this, Environment.DIRECTORY_DOWNLOADS, "SUO-Link.apk");
                req.setMimeType("application/vnd.android.package-archive");
                dm.enqueue(req);
            }
        } catch (Exception ignored) { }
    }

    private void startBeaconDiscovery() {
        beaconListenerThread = new Thread(new BeaconRunnable(this));
        beaconListenerThread.setDaemon(true);
        beaconListenerThread.start();
    }

    @Override
    public boolean dispatchGenericMotionEvent(MotionEvent event) {
        if ((event.getSource() & InputDevice.SOURCE_JOYSTICK) == InputDevice.SOURCE_JOYSTICK &&
                event.getAction() == MotionEvent.ACTION_MOVE) {
            float lx = event.getAxisValue(MotionEvent.AXIS_X);
            float ly = event.getAxisValue(MotionEvent.AXIS_Y);

            String js = String.format("if(window.sendInput){ window.sendInput({type:'stick_vector', x:%f, y:%f}); }", lx, ly);
            webView.post(new EvalJsRunnable(webView, js));
            return true;
        }
        return super.dispatchGenericMotionEvent(event);
    }

    @Override
    public boolean dispatchKeyEvent(KeyEvent event) {
        int action = event.getAction();
        boolean isDown = (action == KeyEvent.ACTION_DOWN);
        int keyCode = event.getKeyCode();

        String mappedKey = mapGamepadKeyCode(keyCode);
        if (mappedKey != null) {
            String type = isDown ? "key_down" : "key_up";
            String js = String.format("if(window.sendInput){ window.sendInput({type:'%s', key:'%s'}); }", type, mappedKey);
            webView.post(new EvalJsRunnable(webView, js));
            return true;
        }

        return super.dispatchKeyEvent(event);
    }

    private String mapGamepadKeyCode(int keyCode) {
        switch (keyCode) {
            case KeyEvent.KEYCODE_BUTTON_A: return "SPACE";
            case KeyEvent.KEYCODE_BUTTON_B: return "ESC";
            case KeyEvent.KEYCODE_BUTTON_X: return "E";
            case KeyEvent.KEYCODE_BUTTON_Y: return "TAB";
            case KeyEvent.KEYCODE_BUTTON_L1: return "SHIFT";
            case KeyEvent.KEYCODE_BUTTON_R1: return "CTRL";
            case KeyEvent.KEYCODE_DPAD_UP: return "UP";
            case KeyEvent.KEYCODE_DPAD_DOWN: return "DOWN";
            case KeyEvent.KEYCODE_DPAD_LEFT: return "LEFT";
            case KeyEvent.KEYCODE_DPAD_RIGHT: return "RIGHT";
            case KeyEvent.KEYCODE_BUTTON_START: return "ENTER";
            case KeyEvent.KEYCODE_BUTTON_SELECT: return "M";
            default: return null;
        }
    }

    private void setImmersiveMode() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.KITKAT) {
            getWindow().getDecorView().setSystemUiVisibility(
                    View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                            | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                            | View.SYSTEM_UI_FLAG_FULLSCREEN
                            | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) setImmersiveMode();
    }

    @Override
    protected void onDestroy() {
        running = false;
        if (beaconListenerThread != null) beaconListenerThread.interrupt();
        if (downloadReceiver != null) {
            try { unregisterReceiver(downloadReceiver); } catch (Exception ignored) { }
        }
        super.onDestroy();
    }
}
